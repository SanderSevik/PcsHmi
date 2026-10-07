using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PcsHmi.Models;

namespace PcsHmi.Services;

/// <summary>
/// SQLite-lagring. Tre tabeller:
///   measurements - logget måling hvert LogIntervalSeconds
///   alarms       - alarmhendelser med start, slutt og kvittering
///   settings     - siste SET/DIM/MODE, sendes til Arduino etter restart
/// Alle tider lagres som UTC i ISO 8601 ("o"-format).
/// </summary>
public class Database
{
    private readonly string _connectionString;

    public Database(IOptions<PcsOptions> options)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.Value.DatabasePath
        }.ToString();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private static string Ts(DateTime utc) => utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    private static DateTime ParseTs(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public void Initialize()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS measurements (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                ts        TEXT    NOT NULL,
                t1        REAL    NOT NULL,
                ldr       INTEGER NOT NULL,
                setpoint  REAL    NOT NULL,
                heat      INTEGER NOT NULL,
                mode      TEXT    NOT NULL,
                dim       INTEGER NOT NULL,
                light     INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_measurements_ts ON measurements(ts);

            CREATE TABLE IF NOT EXISTS alarms (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                type          TEXT    NOT NULL,
                message       TEXT    NOT NULL,
                value         REAL,
                ts_start      TEXT    NOT NULL,
                ts_end        TEXT,
                acknowledged  INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS settings (
                key    TEXT PRIMARY KEY,
                value  TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- målinger
    public void InsertMeasurement(Measurement m)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO measurements (ts, t1, ldr, setpoint, heat, mode, dim, light)
            VALUES ($ts, $t1, $ldr, $sp, $heat, $mode, $dim, $light);
            """;
        cmd.Parameters.AddWithValue("$ts", Ts(m.Timestamp));
        cmd.Parameters.AddWithValue("$t1", m.T1);
        cmd.Parameters.AddWithValue("$ldr", m.Ldr);
        cmd.Parameters.AddWithValue("$sp", m.Setpoint);
        cmd.Parameters.AddWithValue("$heat", m.Heat ? 1 : 0);
        cmd.Parameters.AddWithValue("$mode", m.Mode);
        cmd.Parameters.AddWithValue("$dim", m.Dim);
        cmd.Parameters.AddWithValue("$light", m.Light);
        cmd.ExecuteNonQuery();
    }

    public List<Measurement> GetMeasurementsSince(DateTime fromUtc)
    {
        var list = new List<Measurement>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT ts, t1, ldr, setpoint, heat, mode, dim, light
            FROM measurements WHERE ts >= $from ORDER BY ts;
            """;
        cmd.Parameters.AddWithValue("$from", Ts(fromUtc));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Measurement(
                ParseTs(r.GetString(0)), r.GetDouble(1), r.GetInt32(2), r.GetDouble(3),
                r.GetInt32(4) == 1, r.GetString(5), r.GetInt32(6), r.GetInt32(7)));
        }
        return list;
    }

    // ---------------------------------------------------------------- alarmer
    public long InsertAlarm(string type, string message, double? value, DateTime startUtc)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO alarms (type, message, value, ts_start) VALUES ($type, $msg, $val, $ts);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$msg", message);
        cmd.Parameters.AddWithValue("$val", (object?)value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", Ts(startUtc));
        return (long)cmd.ExecuteScalar()!;
    }

    public void EndAlarm(long id, DateTime endUtc)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE alarms SET ts_end = $ts WHERE id = $id AND ts_end IS NULL;";
        cmd.Parameters.AddWithValue("$ts", Ts(endUtc));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void AcknowledgeAlarm(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE alarms SET acknowledged = 1 WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Alarmer som er aktive eller ikke kvittert (vises i alarmlista).</summary>
    public List<AlarmRecord> GetOpenAlarms() =>
        QueryAlarms("WHERE ts_end IS NULL OR acknowledged = 0 ORDER BY ts_start DESC");

    public List<AlarmRecord> GetAlarmHistory(int limit) =>
        QueryAlarms($"ORDER BY ts_start DESC LIMIT {Math.Clamp(limit, 1, 1000)}");

    private List<AlarmRecord> QueryAlarms(string tail)
    {
        var list = new List<AlarmRecord>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT id, type, message, value, ts_start, ts_end, acknowledged FROM alarms {tail};";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new AlarmRecord(
                r.GetInt64(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetDouble(3),
                ParseTs(r.GetString(4)),
                r.IsDBNull(5) ? null : ParseTs(r.GetString(5)),
                r.GetInt32(6) == 1));
        }
        return list;
    }

    // ---------------------------------------------------------------- innstillinger
    public string? GetSetting(string key)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }
}
