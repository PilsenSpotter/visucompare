# VisuCompare

Autor a vydavatel: **Oliver Bocko**.

Jednoduchá desktopová aplikace pro porovnání dvou obrázků ve Windows. Obrázky vyberte tlačítky nebo je přetáhněte na kartu PŘED či PO. Rozdělovací čáru lze posouvat přímo v náhledu nebo pomocí posuvníku pod ním.

V horní liště lze přepnout světlý a tmavý režim; aplikace si volbu zapamatuje. Vedle něj lze zapnout nebo vypnout automatické ukládání posledního porovnání. Při vypnutém automatickém ukládání zůstane porovnání otevřené do zavření aplikace; `.bfrafr` soubory lze dál ukládat a otevírat ručně.

Režim celé obrazovky zapnete tlačítkem v horní liště nebo klávesou **F11**. Zobrazí pouze porovnání s popisky a dělicí čárou. Ukončíte ho klávesou **Esc** nebo **F11**.

V části **Prezentace** přidáte aktuální porovnání nebo více uložených souborů `.bfrafr` jako snímky. Šipkami **←** a **→** mezi nimi přepínáte. Tlačítko **Uložit prezentaci…** uloží všechny snímky včetně obrázků a polohy dělicí čáry do jediného souboru `.bfrafr`.

Jakmile prezentace obsahuje alespoň jeden snímek, horní tlačítko se změní na **Uložit prezentaci** a také uloží všechny její snímky. Soubor znovu otevřete přes **Otevřít .bfrafr** nebo dvojklikem v Průzkumníku; obnoví se pořadí snímků, názvy obrázků i poloha dělicí čáry na každém snímku.

## Spuštění

Je potřeba .NET 8 SDK a Windows Desktop Runtime.

```powershell
dotnet run
```

Sestavenou aplikaci spustíte souborem `net8.0-windows\VisuCompare.exe`.

Aplikace ukládá kopie obrázků a nastavení do `%LOCALAPPDATA%\PredPo`. Při dalším spuštění obnoví poslední dvojici i polohu posuvníku.

Pro přenos nebo archivaci porovnání použijte tlačítko **Uložit .bfrafr**. Soubor obsahuje oba použité obrázky, jejich názvy a polohu dělící čáry. Jde o soubor projektu, nikoli o běžný obrázek; otevřete ho v aplikaci tlačítkem **Otevřít .bfrafr** nebo dvojklikem v Průzkumníku. Při prvním spuštění aplikace se zaregistruje přípona `.bfrafr` pro tohoto uživatele; pokud Windows nabídne výběr programu, zvolte VisuCompare.
