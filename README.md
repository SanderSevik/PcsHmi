# P-hus HMI (Blazor + SQLite)

Leser data fra Arduinoen over USB, lagrer i SQLite og viser HMI i nettleseren.

## Struktur
- `Models/` – Measurement, AlarmRecord, LineParser (tolker `T1:…,LDR:…`)
- `Services/SerialService.cs` – leser serieport, sender SET/DIM/MODE, gjenoppretter innstillinger etter Arduino-restart
- `Services/Database.cs` – tabellene `measurements`, `alarms`, `settings`
- `Services/AlarmService.cs` – høy/lav temperatur og tapt kommunikasjon
- `Components/Pages/Home.razor` – oversikt og betjening
- `Components/Pages/History.razor` – temperaturgraf og alarmlogg
- `appsettings.json` – serieport, alarmgrenser, loggintervall

## Kjøre på PC (Visual Studio)
Arduino i PC-en: sett riktig COM-port i `appsettings.Development.json`, trykk F5.

## Publisere til Pi-en (fra PC, i prosjektmappa)
    dotnet publish -c Release -r linux-arm64 --self-contained -o publish
    scp -r publish/* group4@<pi-ip>:~/pcshmi/

## Kjøre på Pi-en
    cd ~/pcshmi
    chmod +x PcsHmi
    ./PcsHmi

Åpne http://<pi-ip>:5000 i nettleseren. Bare ett program kan bruke serieporten
om gangen – stopp `cat`/`screen` først.

## Autostart
Se `deploy/pcshmi.service`.
