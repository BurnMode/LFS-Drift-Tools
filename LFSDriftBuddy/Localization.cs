using System;
using System.Collections.Generic;

namespace LFSDriftBuddy
{
    public enum AppLanguage { English, Polish, Turkish, German, Spanish }

    public static class Localization
    {
        public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.English;

        public static event Action LanguageChanged;

        public static void SetLanguage(AppLanguage lang)
        {
            if (CurrentLanguage == lang) return;
            CurrentLanguage = lang;
            LanguageChanged?.Invoke();
        }

        public static string LanguageDisplayName(AppLanguage lang) => lang switch
        {
            AppLanguage.English => "English",
            AppLanguage.Polish => "Polski",
            AppLanguage.Turkish => "Türkçe",
            AppLanguage.German => "Deutsch",
            AppLanguage.Spanish => "Español",
            _ => lang.ToString()
        };

        public static string T(string key)
        {
            if (_strings.TryGetValue(key, out var translations) &&
                translations.TryGetValue(CurrentLanguage, out var value))
                return value;

            if (_strings.TryGetValue(key, out var t2) && t2.TryGetValue(AppLanguage.English, out var en))
                return en;

            return key;
        }

        private static readonly Dictionary<string, Dictionary<AppLanguage, string>> _strings =
            new Dictionary<string, Dictionary<AppLanguage, string>>
            {
                ["header.title"] = new()
                {
                    [AppLanguage.English] = "General",
                    [AppLanguage.Polish] = "Ogólne",
                    [AppLanguage.Turkish] = "Genel",
                    [AppLanguage.German] = "Allgemein",
                    [AppLanguage.Spanish] = "General",
                },
                ["header.colors"] = new()
                {
                    [AppLanguage.English] = "COLORS",
                    [AppLanguage.Polish] = "KOLORY",
                    [AppLanguage.Turkish] = "RENKLER",
                    [AppLanguage.German] = "FARBEN",
                    [AppLanguage.Spanish] = "COLORES",
                },
                ["header.theme"] = new()
                {
                    [AppLanguage.English] = "Dark mode",
                    [AppLanguage.Polish] = "Czarny motyw",
                    [AppLanguage.Turkish] = "Karanlık mod",
                    [AppLanguage.German] = "Dunkler Modus",
                    [AppLanguage.Spanish] = "Modo oscuro",
                },
                ["header.language"] = new()
                {
                    [AppLanguage.English] = "Language",
                    [AppLanguage.Polish] = "Język",
                    [AppLanguage.Turkish] = "Dil",
                    [AppLanguage.German] = "Sprache",
                    [AppLanguage.Spanish] = "Idioma",
                },
                ["header.appversion"] = new()
                {
                    [AppLanguage.English] = "App Version",
                    [AppLanguage.Polish] = "Wersja aplikacji",
                    [AppLanguage.Turkish] = "Uygulama Sürümü",
                    [AppLanguage.German] = "App-Version",
                    [AppLanguage.Spanish] = "Versión de la app",
                },
                ["status.disconnected"] = new()
                {
                    [AppLanguage.English] = "⬤  Disconnected InSim",
                    [AppLanguage.Polish] = "⬤  Rozłączono InSim",
                    [AppLanguage.Turkish] = "⬤  InSim Bağlantısı Kesildi",
                    [AppLanguage.German] = "⬤  InSim getrennt",
                    [AppLanguage.Spanish] = "⬤  InSim desconectado",
                },
                ["status.connected"] = new()
                {
                    [AppLanguage.English] = "⬤  Connected InSim",
                    [AppLanguage.Polish] = "⬤  Połączono InSim",
                    [AppLanguage.Turkish] = "⬤  InSim Bağlandı",
                    [AppLanguage.German] = "⬤  InSim verbunden",
                    [AppLanguage.Spanish] = "⬤  InSim conectado",
                },
                ["status.outgauge.disconnected"] = new()
                {
                    [AppLanguage.English] = "⬤  Disconnected OutGauge",
                    [AppLanguage.Polish] = "⬤  Rozłączono OutGauge",
                    [AppLanguage.Turkish] = "⬤  OutGauge Bağlantısı Kesildi",
                    [AppLanguage.German] = "⬤  OutGauge getrennt",
                    [AppLanguage.Spanish] = "⬤  OutGauge desconectado",
                },
                ["status.outgauge.connected"] = new()
                {
                    [AppLanguage.English] = "⬤  Connected OutGauge",
                    [AppLanguage.Polish] = "⬤  Połączono OutGauge",
                    [AppLanguage.Turkish] = "⬤  OutGauge Bağlandı",
                    [AppLanguage.German] = "⬤  OutGauge verbunden",
                    [AppLanguage.Spanish] = "⬤  OutGauge conectado",
                },
                ["status.hint"] = new()
                {
                    [AppLanguage.English] = "Type /insim 29999 in LFS, and click CONNECT.",
                    [AppLanguage.Polish] = "Wpisz /insim 29999 w LFS i kliknij CONNECT.",
                    [AppLanguage.Turkish] = "LFS içinde /insim 29999 yazın ve CONNECT'e tıklayın.",
                    [AppLanguage.German] = "Gib /insim 29999 in LFS ein und klicke auf CONNECT.",
                    [AppLanguage.Spanish] = "Escribe /insim 29999 en LFS y haz clic en CONNECT.",
                },
                ["status.insim.connect_timeout"] = new()
                {
                    [AppLanguage.English] = "Failed to connect to {0}:{1} — timed out ({2}s).",
                    [AppLanguage.Polish] = "Nie udało się połączyć z {0}:{1} — przekroczono limit czasu ({2}s).",
                    [AppLanguage.Turkish] = "{0}:{1} adresine bağlanılamadı — zaman aşımı ({2}s).",
                    [AppLanguage.German] = "Verbindung zu {0}:{1} fehlgeschlagen — Zeitüberschreitung ({2}s).",
                    [AppLanguage.Spanish] = "No se pudo conectar a {0}:{1} — tiempo de espera agotado ({2}s).",
                },
                ["status.insim.connect_error"] = new()
                {
                    [AppLanguage.English] = "Connection error: {0}",
                    [AppLanguage.Polish] = "Błąd połączenia: {0}",
                    [AppLanguage.Turkish] = "Bağlantı hatası: {0}",
                    [AppLanguage.German] = "Verbindungsfehler: {0}",
                    [AppLanguage.Spanish] = "Error de conexión: {0}",
                },
                ["status.insim.connected"] = new()
                {
                    [AppLanguage.English] = "Connected to LFS on {0}:{1}",
                    [AppLanguage.Polish] = "Połączono z LFS na {0}:{1}",
                    [AppLanguage.Turkish] = "LFS'e {0}:{1} üzerinden bağlanıldı",
                    [AppLanguage.German] = "Mit LFS verbunden auf {0}:{1}",
                    [AppLanguage.Spanish] = "Conectado a LFS en {0}:{1}",
                },
                ["status.insim.disconnected"] = new()
                {
                    [AppLanguage.English] = "Disconnected.",
                    [AppLanguage.Polish] = "Rozłączono.",
                    [AppLanguage.Turkish] = "Bağlantı kesildi.",
                    [AppLanguage.German] = "Getrennt.",
                    [AppLanguage.Spanish] = "Desconectado.",
                },
                ["status.insim.send_error"] = new()
                {
                    [AppLanguage.English] = "Send error: {0}",
                    [AppLanguage.Polish] = "Błąd wysyłania: {0}",
                    [AppLanguage.Turkish] = "Gönderme hatası: {0}",
                    [AppLanguage.German] = "Sendefehler: {0}",
                    [AppLanguage.Spanish] = "Error al enviar: {0}",
                },
                ["status.insim.receive_error"] = new()
                {
                    [AppLanguage.English] = "Receive error: {0}",
                    [AppLanguage.Polish] = "Błąd odbioru: {0}",
                    [AppLanguage.Turkish] = "Alma hatası: {0}",
                    [AppLanguage.German] = "Empfangsfehler: {0}",
                    [AppLanguage.Spanish] = "Error al recibir: {0}",
                },
                ["status.insim.axi_no_layout"] = new()
                {
                    [AppLanguage.English] = "InSim: no layout name reported (it may have been loaded by the host, not locally).",
                    [AppLanguage.Polish] = "InSim: brak nazwy layoutu (mógł zostać wgrany przez hosta, nie lokalnie).",
                    [AppLanguage.Turkish] = "InSim: layout adı bildirilmedi (host tarafından yüklenmiş olabilir, yerel olarak değil).",
                    [AppLanguage.German] = "InSim: kein Layout-Name gemeldet (könnte vom Host geladen worden sein, nicht lokal).",
                    [AppLanguage.Spanish] = "InSim: no se informó el nombre del layout (puede haberlo cargado el host, no localmente).",
                },
                ["status.insim.axi_layout_detected"] = new()
                {
                    [AppLanguage.English] = "InSim: detected layout '{0}'",
                    [AppLanguage.Polish] = "InSim: wykryto layout '{0}'",
                    [AppLanguage.Turkish] = "InSim: layout algılandı '{0}'",
                    [AppLanguage.German] = "InSim: Layout erkannt '{0}'",
                    [AppLanguage.Spanish] = "InSim: layout detectado '{0}'",
                },
                ["status.outgauge.udp_open_error"] = new()
                {
                    [AppLanguage.English] = "Could not open UDP {0}: {1}",
                    [AppLanguage.Polish] = "Nie można otworzyć UDP {0}: {1}",
                    [AppLanguage.Turkish] = "UDP {0} açılamadı: {1}",
                    [AppLanguage.German] = "UDP {0} konnte nicht geöffnet werden: {1}",
                    [AppLanguage.Spanish] = "No se pudo abrir UDP {0}: {1}",
                },
                ["status.outgauge.receive_error"] = new()
                {
                    [AppLanguage.English] = "OutGauge error: {0}",
                    [AppLanguage.Polish] = "Błąd OutGauge: {0}",
                    [AppLanguage.Turkish] = "OutGauge hatası: {0}",
                    [AppLanguage.German] = "OutGauge-Fehler: {0}",
                    [AppLanguage.Spanish] = "Error de OutGauge: {0}",
                },
                ["status.revlimiter.forced_restore"] = new()
                {
                    [AppLanguage.English] = "Rev limiter: forcing ignition back on ({0}) — engine got stuck off.",
                    [AppLanguage.Polish] = "Rev limiter: wymuszam przywrócenie zapłonu ({0}) — silnik utknął zgaszony.",
                    [AppLanguage.Turkish] = "Devir limiteri: kontak zorla açılıyor ({0}) — motor kapalı takıldı.",
                    [AppLanguage.German] = "Drehzahlbegrenzer: erzwinge Zündung wieder ein ({0}) — Motor blieb aus.",
                    [AppLanguage.Spanish] = "Limitador de revoluciones: forzando el encendido ({0}) — el motor se quedó apagado.",
                },
                ["status.revlimiter.no_window"] = new()
                {
                    [AppLanguage.English] = "LFS window not found (rev limiter)",
                    [AppLanguage.Polish] = "Nie znaleziono okna LFS (rev limiter)",
                    [AppLanguage.Turkish] = "LFS penceresi bulunamadı (devir limiteri)",
                    [AppLanguage.German] = "LFS-Fenster nicht gefunden (Drehzahlbegrenzer)",
                    [AppLanguage.Spanish] = "No se encontró la ventana de LFS (limitador de revoluciones)",
                },
                ["status.driver_load_error"] = new()
                {
                    [AppLanguage.English] = "Could not load save data for driver '{0}': {1}",
                    [AppLanguage.Polish] = "Nie udało się wczytać zapisu kierowcy '{0}': {1}",
                    [AppLanguage.Turkish] = "'{0}' sürücüsünün kayıt verisi yüklenemedi: {1}",
                    [AppLanguage.German] = "Speicherdaten für Fahrer '{0}' konnten nicht geladen werden: {1}",
                    [AppLanguage.Spanish] = "No se pudieron cargar los datos guardados del piloto '{0}': {1}",
                },
                ["status.driver_save_error"] = new()
                {
                    [AppLanguage.English] = "Could not save data for driver '{0}': {1}",
                    [AppLanguage.Polish] = "Nie udało się zapisać danych kierowcy '{0}': {1}",
                    [AppLanguage.Turkish] = "'{0}' sürücüsünün verisi kaydedilemedi: {1}",
                    [AppLanguage.German] = "Daten für Fahrer '{0}' konnten nicht gespeichert werden: {1}",
                    [AppLanguage.Spanish] = "No se pudieron guardar los datos del piloto '{0}': {1}",
                },
                ["connection.title"] = new()
                {
                    [AppLanguage.English] = "InSim Connection",
                    [AppLanguage.Polish] = "Połączenie InSim",
                    [AppLanguage.Turkish] = "InSim Bağlantısı",
                    [AppLanguage.German] = "InSim-Verbindung",
                    [AppLanguage.Spanish] = "Conexión InSim",
                },
                ["connection.outgauge.title"] = new()
                {
                    [AppLanguage.English] = "OutGauge Listening",
                    [AppLanguage.Polish] = "Nasłuchiwanie OutGauge",
                    [AppLanguage.Turkish] = "OutGauge Dinleme",
                    [AppLanguage.German] = "OutGauge-Abhören",
                    [AppLanguage.Spanish] = "Escucha de OutGauge",
                },
                ["connection.outgauge.subtitle"] = new()
                {
                    [AppLanguage.English] = "Listens locally for LFS's OutGauge broadcast (no host needed) — match the port to OutGauge Port in LFS cfg.txt.",
                    [AppLanguage.Polish] = "Nasłuchuje lokalnie transmisji OutGauge z LFS (host nie jest potrzebny) — dopasuj port do OutGauge Port w cfg.txt LFS.",
                    [AppLanguage.Turkish] = "LFS'in OutGauge yayınını yerel olarak dinler (host gerekmez) — portu LFS cfg.txt dosyasındaki OutGauge Port ile eşleştirin.",
                    [AppLanguage.German] = "Empfängt lokal die OutGauge-Übertragung von LFS (kein Host nötig) — den Port an OutGauge Port in der LFS cfg.txt anpassen.",
                    [AppLanguage.Spanish] = "Escucha localmente la transmisión OutGauge de LFS (no se necesita host); haz coincidir el puerto con OutGauge Port en cfg.txt de LFS.",
                },
                ["connection.status"] = new()
                {
                    [AppLanguage.English] = "STATUS",
                    [AppLanguage.Polish] = "STATUS",
                    [AppLanguage.Turkish] = "DURUM",
                    [AppLanguage.German] = "STATUS",
                    [AppLanguage.Spanish] = "ESTADO",
                },
                ["connection.host"] = new()
                {
                    [AppLanguage.English] = "Host",
                    [AppLanguage.Polish] = "Host",
                    [AppLanguage.Turkish] = "Host",
                    [AppLanguage.German] = "Host",
                    [AppLanguage.Spanish] = "Host",
                },
                ["connection.port"] = new()
                {
                    [AppLanguage.English] = "Port",
                    [AppLanguage.Polish] = "Port",
                    [AppLanguage.Turkish] = "Port",
                    [AppLanguage.German] = "Port",
                    [AppLanguage.Spanish] = "Puerto",
                },
                ["connection.adminpass"] = new()
                {
                    [AppLanguage.English] = "Admin password",
                    [AppLanguage.Polish] = "Hasło admina",
                    [AppLanguage.Turkish] = "Yönetici şifresi",
                    [AppLanguage.German] = "Admin-Passwort",
                    [AppLanguage.Spanish] = "Contraseña de administrador",
                },
                ["speedometer.title"] = new()
                {
                    [AppLanguage.English] = "Speedometer",
                    [AppLanguage.Polish] = "Prędkościomierz",
                    [AppLanguage.Turkish] = "Hız Göstergesi",
                    [AppLanguage.German] = "Tachometer",
                    [AppLanguage.Spanish] = "Velocímetro",
                },
                ["speedometer.speed"] = new()
                {
                    [AppLanguage.English] = "SPEED",
                    [AppLanguage.Polish] = "PRĘDKOŚĆ",
                    [AppLanguage.Turkish] = "HIZ",
                    [AppLanguage.German] = "GESCHWINDIGKEIT",
                    [AppLanguage.Spanish] = "VELOCIDAD",
                },
                ["speedometer.unit"] = new()
                {
                    [AppLanguage.English] = "km/h",
                    [AppLanguage.Polish] = "km/h",
                    [AppLanguage.Turkish] = "km/sa",
                    [AppLanguage.German] = "km/h",
                    [AppLanguage.Spanish] = "km/h",
                },
                ["speedometer.showhud"] = new()
                {
                    [AppLanguage.English] = "Show Speedo+Tacho HUD",
                    [AppLanguage.Polish] = "Pokaż HUD prędkościomierz+obrotomierz",
                    [AppLanguage.Turkish] = "Hız+Devir HUD'unu göster",
                    [AppLanguage.German] = "Tacho+Drehzahl-HUD anzeigen",
                    [AppLanguage.Spanish] = "Mostrar HUD de velocímetro+tacómetro",
                },
                ["speedometer.offsetx"] = new()
                {
                    [AppLanguage.English] = "Offset X:",
                    [AppLanguage.Polish] = "Przesunięcie X:",
                    [AppLanguage.Turkish] = "Kaydırma X:",
                    [AppLanguage.German] = "Versatz X:",
                    [AppLanguage.Spanish] = "Desplazamiento X:",
                },
                ["speedometer.offsety"] = new()
                {
                    [AppLanguage.English] = "Offset Y:",
                    [AppLanguage.Polish] = "Przesunięcie Y:",
                    [AppLanguage.Turkish] = "Kaydırma Y:",
                    [AppLanguage.German] = "Versatz Y:",
                    [AppLanguage.Spanish] = "Desplazamiento Y:",
                },
                ["speedometer.scale"] = new()
                {
                    [AppLanguage.English] = "Scale:",
                    [AppLanguage.Polish] = "Skala:",
                    [AppLanguage.Turkish] = "Ölçek:",
                    [AppLanguage.German] = "Skalierung:",
                    [AppLanguage.Spanish] = "Escala:",
                },
                ["speedometer.usemph"] = new()
                {
                    [AppLanguage.English] = "Use MPH",
                    [AppLanguage.Polish] = "Użyj MPH",
                    [AppLanguage.Turkish] = "MPH kullan",
                    [AppLanguage.German] = "MPH verwenden",
                    [AppLanguage.Spanish] = "Usar MPH",
                },
                ["speedometer.usemph.subtitle"] = new()
                {
                    [AppLanguage.English] = "Switches the speed unit from km/h to mph, both here and on the in-game HUD.",
                    [AppLanguage.Polish] = "Przełącza jednostkę prędkości z km/h na mph, zarówno tutaj, jak i w HUD-zie w grze.",
                    [AppLanguage.Turkish] = "Hız birimini hem burada hem de oyun içi HUD'da km/sa'den mph'ye çevirir.",
                    [AppLanguage.German] = "Wechselt die Geschwindigkeitseinheit von km/h zu mph, sowohl hier als auch im Ingame-HUD.",
                    [AppLanguage.Spanish] = "Cambia la unidad de velocidad de km/h a mph, tanto aquí como en el HUD del juego.",
                },
                ["speedometer.preview"] = new()
                {
                    [AppLanguage.English] = "Preview",
                    [AppLanguage.Polish] = "Podgląd",
                    [AppLanguage.Turkish] = "Canlı",
                    [AppLanguage.German] = "Vorschau",
                    [AppLanguage.Spanish] = "Vista previa",
                },
                ["speedometer.preset.label"] = new()
                {
                    [AppLanguage.English] = "PRESET {0}",
                    [AppLanguage.Polish] = "PRESET {0}",
                    [AppLanguage.Turkish] = "PRESET {0}",
                    [AppLanguage.German] = "PRESET {0}",
                    [AppLanguage.Spanish] = "AJUSTE {0}",
                },
                ["speedometer.preset.reset"] = new()
                {
                    [AppLanguage.English] = "Reset Preset",
                    [AppLanguage.Polish] = "Resetuj preset",
                    [AppLanguage.Turkish] = "Ön Ayarı Sıfırla",
                    [AppLanguage.German] = "Preset zurücksetzen",
                    [AppLanguage.Spanish] = "Restablecer ajuste",
                },
                ["speedometer.background.button"] = new()
                {
                    [AppLanguage.English] = "Background",
                    [AppLanguage.Polish] = "Tło",
                    [AppLanguage.Turkish] = "Arka Plan",
                    [AppLanguage.German] = "Hintergrund",
                    [AppLanguage.Spanish] = "Fondo",
                },
                ["speedometer.background.title"] = new()
                {
                    [AppLanguage.English] = "Speedometer Background",
                    [AppLanguage.Polish] = "Tło prędkościomierza",
                    [AppLanguage.Turkish] = "Hız Göstergesi Arka Planı",
                    [AppLanguage.German] = "Tacho-Hintergrund",
                    [AppLanguage.Spanish] = "Fondo del velocímetro",
                },
                ["speedometer.background.subtitle"] = new()
                {
                    [AppLanguage.English] = "Import a PNG, drag to reposition and scroll to zoom, then pick the framing shown inside the circle.",
                    [AppLanguage.Polish] = "Zaimportuj PNG, przeciągnij aby przesunąć i użyj scrolla aby przybliżyć, wybierz kadr widoczny w kole.",
                    [AppLanguage.Turkish] = "Bir PNG içe aktarın, konumlandırmak için sürükleyin ve yakınlaştırmak için kaydırın; çember içinde görünen kadrajı seçin.",
                    [AppLanguage.German] = "Importiere ein PNG, ziehe es zum Verschieben und scrolle zum Zoomen, wähle den im Kreis sichtbaren Ausschnitt.",
                    [AppLanguage.Spanish] = "Importa un PNG, arrastra para reposicionar y usa el scroll para hacer zoom; elige el encuadre visible dentro del círculo.",
                },
                ["speedometer.background.zoom"] = new()
                {
                    [AppLanguage.English] = "Zoom",
                    [AppLanguage.Polish] = "Przybliżenie",
                    [AppLanguage.Turkish] = "Yakınlaştırma",
                    [AppLanguage.German] = "Zoom",
                    [AppLanguage.Spanish] = "Zoom",
                },
                ["speedometer.background.opacity"] = new()
                {
                    [AppLanguage.English] = "Opacity",
                    [AppLanguage.Polish] = "Przezroczystość",
                    [AppLanguage.Turkish] = "Opaklık",
                    [AppLanguage.German] = "Deckkraft",
                    [AppLanguage.Spanish] = "Opacidad",
                },
                ["speedometer.background.import"] = new()
                {
                    [AppLanguage.English] = "Import PNG",
                    [AppLanguage.Polish] = "Importuj PNG",
                    [AppLanguage.Turkish] = "PNG İçe Aktar",
                    [AppLanguage.German] = "PNG importieren",
                    [AppLanguage.Spanish] = "Importar PNG",
                },
                ["speedometer.background.remove"] = new()
                {
                    [AppLanguage.English] = "Remove Image",
                    [AppLanguage.Polish] = "Usuń obraz",
                    [AppLanguage.Turkish] = "Görseli Kaldır",
                    [AppLanguage.German] = "Bild entfernen",
                    [AppLanguage.Spanish] = "Quitar imagen",
                },
                ["speedometer.elements.button"] = new()
                {
                    [AppLanguage.English] = "UI Elements",
                    [AppLanguage.Polish] = "Elementy UI",
                    [AppLanguage.Turkish] = "Arayüz Öğeleri",
                    [AppLanguage.German] = "UI-Elemente",
                    [AppLanguage.Spanish] = "Elementos de la UI",
                },
                ["speedometer.elements.title"] = new()
                {
                    [AppLanguage.English] = "Speedometer UI Elements",
                    [AppLanguage.Polish] = "Elementy UI prędkościomierza",
                    [AppLanguage.Turkish] = "Hız Göstergesi Arayüz Öğeleri",
                    [AppLanguage.German] = "Tacho-UI-Elemente",
                    [AppLanguage.Spanish] = "Elementos de la UI del velocímetro",
                },
                ["speedometer.elements.subtitle"] = new()
                {
                    [AppLanguage.English] = "Show or hide individual parts of the gauge.",
                    [AppLanguage.Polish] = "Pokaż lub ukryj poszczególne elementy zegara.",
                    [AppLanguage.Turkish] = "Göstergenin ayrı ayrı bölümlerini gösterin veya gizleyin.",
                    [AppLanguage.German] = "Einzelne Teile der Anzeige ein- oder ausblenden.",
                    [AppLanguage.Spanish] = "Muestra u oculta partes individuales del indicador.",
                },
                ["speedometer.elements.rpmdigits"] = new()
                {
                    [AppLanguage.English] = "RPM digits",
                    [AppLanguage.Polish] = "Cyfry obrotów",
                    [AppLanguage.Turkish] = "Devir rakamları",
                    [AppLanguage.German] = "Drehzahlziffern",
                    [AppLanguage.Spanish] = "Dígitos de RPM",
                },
                ["speedometer.elements.unit"] = new()
                {
                    [AppLanguage.English] = "Speed unit (km/h / mph)",
                    [AppLanguage.Polish] = "Jednostka prędkości (km/h / mph)",
                    [AppLanguage.Turkish] = "Hız birimi (km/sa / mph)",
                    [AppLanguage.German] = "Geschwindigkeitseinheit (km/h / mph)",
                    [AppLanguage.Spanish] = "Unidad de velocidad (km/h / mph)",
                },
                ["speedometer.elements.minorticks"] = new()
                {
                    [AppLanguage.English] = "Minor tick marks",
                    [AppLanguage.Polish] = "Kreski mniejszych obrotów",
                    [AppLanguage.Turkish] = "Küçük çizgiler",
                    [AppLanguage.German] = "Kleine Skalenstriche",
                    [AppLanguage.Spanish] = "Marcas menores",
                },
                ["speedometer.elements.majorticks"] = new()
                {
                    [AppLanguage.English] = "Major tick marks",
                    [AppLanguage.Polish] = "Kreski większych obrotów",
                    [AppLanguage.Turkish] = "Büyük çizgiler",
                    [AppLanguage.German] = "Große Skalenstriche",
                    [AppLanguage.Spanish] = "Marcas mayores",
                },
                ["speedometer.elements.redlinethickness"] = new()
                {
                    [AppLanguage.English] = "Redline thickness",
                    [AppLanguage.Polish] = "Grubość redline",
                    [AppLanguage.Turkish] = "Kırmızı çizgi kalınlığı",
                    [AppLanguage.German] = "Rote-Linie-Dicke",
                    [AppLanguage.Spanish] = "Grosor de la zona roja",
                },
                ["score.title"] = new()
                {
                    [AppLanguage.English] = "Drift Score",
                    [AppLanguage.Polish] = "Wynik driftu",
                    [AppLanguage.Turkish] = "Drift Puanı",
                    [AppLanguage.German] = "Drift-Punktzahl",
                    [AppLanguage.Spanish] = "Puntuación de derrape",
                },
                ["score.total"] = new()
                {
                    [AppLanguage.English] = "TOTAL SCORE",
                    [AppLanguage.Polish] = "WYNIK CAŁKOWITY",
                    [AppLanguage.Turkish] = "TOPLAM PUAN",
                    [AppLanguage.German] = "GESAMTPUNKTZAHL",
                    [AppLanguage.Spanish] = "PUNTUACIÓN TOTAL",
                },
                ["hud.lapscore"] = new()
                {
                    [AppLanguage.English] = "LAP SCORE",
                    [AppLanguage.Polish] = "WYNIK OKRĄŻENIA",
                    [AppLanguage.Turkish] = "TUR PUANI",
                    [AppLanguage.German] = "RUNDENPUNKTZAHL",
                    [AppLanguage.Spanish] = "PUNTUACIÓN DE VUELTA",
                },
                ["hud.bestlapscore"] = new()
                {
                    [AppLanguage.English] = "BEST LAP SCORE",
                    [AppLanguage.Polish] = "NAJLEPSZY WYNIK",
                    [AppLanguage.Turkish] = "EN İYİ TUR PUANI",
                    [AppLanguage.German] = "BESTE RUNDENPUNKTZAHL",
                    [AppLanguage.Spanish] = "MEJOR PUNTUACIÓN DE VUELTA",
                },
                ["score.run"] = new()
                {
                    [AppLanguage.English] = "RUN SCORE",
                    [AppLanguage.Polish] = "WYNIK PRZEJAZDU",
                    [AppLanguage.Turkish] = "TUR PUANI",
                    [AppLanguage.German] = "LAUFPUNKTZAHL",
                    [AppLanguage.Spanish] = "PUNTUACIÓN DE LA CARRERA",
                },
                ["score.combo"] = new()
                {
                    [AppLanguage.English] = "COMBO",
                    [AppLanguage.Polish] = "KOMBO",
                    [AppLanguage.Turkish] = "KOMBO",
                    [AppLanguage.German] = "KOMBO",
                    [AppLanguage.Spanish] = "COMBO",
                },
                ["score.angle"] = new()
                {
                    [AppLanguage.English] = "DRIFT ANGLE",
                    [AppLanguage.Polish] = "KĄT DRIFTU",
                    [AppLanguage.Turkish] = "DRIFT AÇISI",
                    [AppLanguage.German] = "DRIFTWINKEL",
                    [AppLanguage.Spanish] = "ÁNGULO DE DERRAPE",
                },
                ["score.reset"] = new()
                {
                    [AppLanguage.English] = "RESET SCORE",
                    [AppLanguage.Polish] = "RESETUJ WYNIK",
                    [AppLanguage.Turkish] = "PUANI SIFIRLA",
                    [AppLanguage.German] = "PUNKTZAHL ZURÜCKSETZEN",
                    [AppLanguage.Spanish] = "REINICIAR PUNTUACIÓN",
                },

                ["score.driver"] = new()
                {
                    [AppLanguage.English] = "Driver: {0}",
                    [AppLanguage.Polish] = "Kierowca: {0}",
                    [AppLanguage.Turkish] = "Sürücü: {0}",
                    [AppLanguage.German] = "Fahrer: {0}",
                    [AppLanguage.Spanish] = "Piloto: {0}",
                },
                ["score.bestrun"] = new()
                {
                    [AppLanguage.English] = "BEST RUN",
                    [AppLanguage.Polish] = "NAJLEPSZY PRZEJAZD",
                    [AppLanguage.Turkish] = "EN İYİ KOŞU",
                    [AppLanguage.German] = "BESTER LAUF",
                    [AppLanguage.Spanish] = "MEJOR TIRADA",
                },
                ["score.bestdrift"] = new()
                {
                    [AppLanguage.English] = "BEST DRIFT",
                    [AppLanguage.Polish] = "NAJDŁUŻSZY DRIFT",
                    [AppLanguage.Turkish] = "EN UZUN DRİFT",
                    [AppLanguage.German] = "LÄNGSTER DRIFT",
                    [AppLanguage.Spanish] = "DERRAPE MÁS LARGO",
                },
                ["score.bestdeepdrift"] = new()
                {
                    [AppLanguage.English] = "BEST DEEP DRIFT",
                    [AppLanguage.Polish] = "NAJDŁUŻSZY GŁĘBOKI DRIFT",
                    [AppLanguage.Turkish] = "EN UZUN DERİN DRİFT",
                    [AppLanguage.German] = "LÄNGSTER TIEFER DRIFT",
                    [AppLanguage.Spanish] = "DERRAPE PROFUNDO MÁS LARGO",
                },
                ["score.mindriftspeed"] = new()
                {
                    [AppLanguage.English] = "Minimum drift speed:",
                    [AppLanguage.Polish] = "Minimalna prędkość driftu:",
                    [AppLanguage.Turkish] = "Minimum drift hızı:",
                    [AppLanguage.German] = "Mindest-Driftgeschwindigkeit:",
                    [AppLanguage.Spanish] = "Velocidad mínima de derrape:",
                },
                ["score.mindriftspeed.subtitle"] = new()
                {
                    [AppLanguage.English] = "Below this speed, a drift won't be detected at all, no matter the slip angle.",
                    [AppLanguage.Polish] = "Poniżej tej prędkości drift w ogóle nie zostanie wykryty, niezależnie od kąta poślizgu.",
                    [AppLanguage.Turkish] = "Bu hızın altında, kayma açısı ne olursa olsun drift hiç algılanmaz.",
                    [AppLanguage.German] = "Unterhalb dieser Geschwindigkeit wird ein Drift gar nicht erkannt, unabhängig vom Schräglaufwinkel.",
                    [AppLanguage.Spanish] = "Por debajo de esta velocidad, no se detectará ningún derrape, sin importar el ángulo de deslizamiento.",
                },
                ["score.maxburnoutspeed"] = new()
                {
                    [AppLanguage.English] = "Maximum burnout speed:",
                    [AppLanguage.Polish] = "Maksymalna prędkość burnoutu:",
                    [AppLanguage.Turkish] = "Maksimum yakış (burnout) hızı:",
                    [AppLanguage.German] = "Maximale Burnout-Geschwindigkeit:",
                    [AppLanguage.Spanish] = "Velocidad máxima de burnout:",
                },
                ["score.maxburnoutspeed.subtitle"] = new()
                {
                    [AppLanguage.English] = "Above this speed, wheelspin counts as regular driving/drift instead of a burnout.",
                    [AppLanguage.Polish] = "Powyżej tej prędkości buksowanie liczy się jako zwykła jazda/drift, a nie jako burnout.",
                    [AppLanguage.Turkish] = "Bu hızın üzerinde patinaj, yakış (burnout) yerine normal sürüş/drift olarak sayılır.",
                    [AppLanguage.German] = "Oberhalb dieser Geschwindigkeit zählt Durchdrehen als normales Fahren/Driften statt als Burnout.",
                    [AppLanguage.Spanish] = "Por encima de esta velocidad, el patinaje cuenta como conducción/derrape normal en vez de burnout.",
                },
                ["score.levels.button"] = new()
                {
                    [AppLanguage.English] = "DRIFT LEVELS",
                    [AppLanguage.Polish] = "POZIOMY DRIFTU",
                    [AppLanguage.Turkish] = "DRİFT SEVİYELERİ",
                    [AppLanguage.German] = "DRIFT-STUFEN",
                    [AppLanguage.Spanish] = "NIVELES DE DERRAPE",
                },
                ["score.levels.title"] = new()
                {
                    [AppLanguage.English] = "Drift Level Settings",
                    [AppLanguage.Polish] = "Ustawienia poziomów driftu",
                    [AppLanguage.Turkish] = "Drift Seviyesi Ayarları",
                    [AppLanguage.German] = "Drift-Stufen-Einstellungen",
                    [AppLanguage.Spanish] = "Ajustes de niveles de derrape",
                },
                ["score.levels.angle_subtitle"] = new()
                {
                    [AppLanguage.English] = "Set the angle at which each drift level starts.",
                    [AppLanguage.Polish] = "Ustaw kąt, przy którym zaczyna się każdy poziom driftu.",
                    [AppLanguage.Turkish] = "Her drift seviyesinin başladığı açıyı ayarlayın.",
                    [AppLanguage.German] = "Lege den Winkel fest, bei dem jede Drift-Stufe beginnt.",
                    [AppLanguage.Spanish] = "Define el ángulo al que empieza cada nivel de derrape.",
                },
                ["score.levels.speed_button"] = new()
                {
                    [AppLanguage.English] = "SPEED LEVELS",
                    [AppLanguage.Polish] = "POZIOMY PRĘDKOŚCI",
                    [AppLanguage.Turkish] = "HIZ SEVİYELERİ",
                    [AppLanguage.German] = "GESCHWINDIGKEITSSTUFEN",
                    [AppLanguage.Spanish] = "NIVELES DE VELOCIDAD",
                },
                ["score.levels.speed_title"] = new()
                {
                    [AppLanguage.English] = "Speed Level Settings",
                    [AppLanguage.Polish] = "Ustawienia poziomów prędkości",
                    [AppLanguage.Turkish] = "Hız Seviyesi Ayarları",
                    [AppLanguage.German] = "Geschwindigkeitsstufen-Einstellungen",
                    [AppLanguage.Spanish] = "Ajustes de niveles de velocidad",
                },
                ["score.levels.speed_subtitle"] = new()
                {
                    [AppLanguage.English] = "Set the speed at which each speed level starts.",
                    [AppLanguage.Polish] = "Ustaw prędkość, przy której zaczyna się każdy poziom prędkości.",
                    [AppLanguage.Turkish] = "Her hız seviyesinin başladığı hızı ayarlayın.",
                    [AppLanguage.German] = "Lege die Geschwindigkeit fest, bei der jede Geschwindigkeitsstufe beginnt.",
                    [AppLanguage.Spanish] = "Define la velocidad a la que empieza cada nivel de velocidad.",
                },
                ["score.levels.angle_header"] = new()
                {
                    [AppLanguage.English] = "ANGLE (DEGREES)",
                    [AppLanguage.Polish] = "KĄT (STOPNIE)",
                    [AppLanguage.Turkish] = "AÇI (DERECE)",
                    [AppLanguage.German] = "WINKEL (GRAD)",
                    [AppLanguage.Spanish] = "ÁNGULO (GRADOS)",
                },
                ["score.levels.angle_base"] = new()
                {
                    [AppLanguage.English] = "Drift entry threshold",
                    [AppLanguage.Polish] = "Próg wejścia w drift",
                    [AppLanguage.Turkish] = "Drift başlangıç eşiği",
                    [AppLanguage.German] = "Drift-Einstiegsschwelle",
                    [AppLanguage.Spanish] = "Umbral de entrada al derrape",
                },
                ["score.levels.angle_backward"] = new()
                {
                    [AppLanguage.English] = "Backward drift threshold",
                    [AppLanguage.Polish] = "Próg driftu tyłem",
                    [AppLanguage.Turkish] = "Geriye drift eşiği",
                    [AppLanguage.German] = "Rückwärtsdrift-Schwelle",
                    [AppLanguage.Spanish] = "Umbral de derrape hacia atrás",
                },
                ["score.levels.speed_header"] = new()
                {
                    [AppLanguage.English] = "SPEED (KM/H)",
                    [AppLanguage.Polish] = "PRĘDKOŚĆ (KM/H)",
                    [AppLanguage.Turkish] = "HIZ (KM/SA)",
                    [AppLanguage.German] = "GESCHWINDIGKEIT (KM/H)",
                    [AppLanguage.Spanish] = "VELOCIDAD (KM/H)",
                },
                ["score.levels.speed_base"] = new()
                {
                    [AppLanguage.English] = "Fast driving threshold",
                    [AppLanguage.Polish] = "Próg szybkiej jazdy",
                    [AppLanguage.Turkish] = "Hızlı sürüş eşiği",
                    [AppLanguage.German] = "Schwelle für schnelles Fahren",
                    [AppLanguage.Spanish] = "Umbral de conducción rápida",
                },
                ["score.levels.speed_level"] = new()
                {
                    [AppLanguage.English] = "Speed level {0}",
                    [AppLanguage.Polish] = "Poziom prędkości {0}",
                    [AppLanguage.Turkish] = "Hız seviyesi {0}",
                    [AppLanguage.German] = "Geschwindigkeitsstufe {0}",
                    [AppLanguage.Spanish] = "Nivel de velocidad {0}",
                },
                ["score.collisiondetect"] = new()
                {
                    [AppLanguage.English] = "Car collision detection",
                    [AppLanguage.Polish] = "Wykrywanie uderzeń w pojazdy",
                    [AppLanguage.Turkish] = "Araç çarpışma algılama",
                    [AppLanguage.German] = "Fahrzeugkollisionserkennung",
                    [AppLanguage.Spanish] = "Detección de colisiones con vehículos",
                },
                ["score.collisiondetect.subtitle"] = new()
                {
                    [AppLanguage.English] = "A light touch that doesn't break a stable drift scores a bonus; a hit that ends it, spins you, or is simply too hard costs points.",
                    [AppLanguage.Polish] = "Lekkie muśnięcie, które nie przerywa stabilnego driftu, daje bonus; uderzenie które go kończy, obraca auto lub jest po prostu za mocne, kosztuje punkty.",
                    [AppLanguage.Turkish] = "Stabil bir driftı bozmayan hafif bir dokunuş bonus kazandırır; driftı bitiren, döndüren veya çok sert olan bir çarpma puan kaybettirir.",
                    [AppLanguage.German] = "Eine leichte Berührung, die einen stabilen Drift nicht unterbricht, gibt einen Bonus; ein Treffer, der ihn beendet, dich dreht oder einfach zu hart ist, kostet Punkte.",
                    [AppLanguage.Spanish] = "Un roce leve que no rompe un derrape estable da un bono; un golpe que lo termina, te hace girar o es simplemente demasiado fuerte cuesta puntos.",
                },
                ["score.objectcollisiondetect"] = new()
                {
                    [AppLanguage.English] = "Object collision detection",
                    [AppLanguage.Polish] = "Wykrywanie uderzeń w obiekty",
                    [AppLanguage.Turkish] = "Nesne çarpışma algılama",
                    [AppLanguage.German] = "Objektkollisionserkennung",
                    [AppLanguage.Spanish] = "Detección de colisiones con objetos",
                },
                ["score.objectcollisiondetect.subtitle"] = new()
                {
                    [AppLanguage.English] = "Detects hits against track objects (walls, cones, etc.) and applies the existing kiss/penalty scoring.",
                    [AppLanguage.Polish] = "Wykrywa uderzenia w obiekty na torze (ściany, pachołki itp.) i nalicza istniejące punkty za muśnięcie/karę.",
                    [AppLanguage.Turkish] = "Pist nesnelerine (duvarlar, koniler vb.) çarpmaları algılar ve mevcut sıyırma/ceza puanlamasını uygular.",
                    [AppLanguage.German] = "Erkennt Treffer gegen Streckenobjekte (Wände, Pylonen usw.) und wendet die bestehende Streifer-/Strafpunktevergabe an.",
                    [AppLanguage.Spanish] = "Detecta golpes contra objetos del circuito (muros, conos, etc.) y aplica la puntuación existente de roce/penalización.",
                },
                ["derby.levels.button"] = new()
                {
                    [AppLanguage.English] = "HIT LEVELS",
                    [AppLanguage.Polish] = "POZIOMY UDERZEŃ",
                    [AppLanguage.Turkish] = "ÇARPMA SEVİYELERİ",
                    [AppLanguage.German] = "AUFPRALLSTUFEN",
                    [AppLanguage.Spanish] = "NIVELES DE IMPACTO",
                },
                ["derby.levels.title"] = new()
                {
                    [AppLanguage.English] = "Hit Level Settings",
                    [AppLanguage.Polish] = "Ustawienia poziomów uderzeń",
                    [AppLanguage.Turkish] = "Çarpma Seviyesi Ayarları",
                    [AppLanguage.German] = "Aufprallstufen-Einstellungen",
                    [AppLanguage.Spanish] = "Ajustes de niveles de impacto",
                },
                ["derby.levels.subtitle"] = new()
                {
                    [AppLanguage.English] = "Set the closing speed at which each hit level starts.",
                    [AppLanguage.Polish] = "Ustaw prędkość zderzenia, przy której zaczyna się każdy poziom uderzenia.",
                    [AppLanguage.Turkish] = "Her çarpma seviyesinin başladığı yaklaşma hızını ayarlayın.",
                    [AppLanguage.German] = "Lege die Aufprallgeschwindigkeit fest, bei der jede Stufe beginnt.",
                    [AppLanguage.Spanish] = "Define la velocidad de choque a la que empieza cada nivel de impacto.",
                },
                ["derby.levels.tier1"] = new()
                {
                    [AppLanguage.English] = "TOUCH",
                    [AppLanguage.Polish] = "STYK",
                    [AppLanguage.Turkish] = "DOKUNMA",
                    [AppLanguage.German] = "BERÜHRUNG",
                    [AppLanguage.Spanish] = "ROCE",
                },
                ["derby.levels.tier2"] = new()
                {
                    [AppLanguage.English] = "BUMP",
                    [AppLanguage.Polish] = "ZDERZENIE",
                    [AppLanguage.Turkish] = "ÇARPMA",
                    [AppLanguage.German] = "STOSS",
                    [AppLanguage.Spanish] = "GOLPE",
                },
                ["derby.levels.tier3"] = new()
                {
                    [AppLanguage.English] = "HIT",
                    [AppLanguage.Polish] = "UDERZENIE",
                    [AppLanguage.Turkish] = "VURUŞ",
                    [AppLanguage.German] = "TREFFER",
                    [AppLanguage.Spanish] = "IMPACTO",
                },
                ["derby.levels.tier4"] = new()
                {
                    [AppLanguage.English] = "SMASH",
                    [AppLanguage.Polish] = "ROZBICIE",
                    [AppLanguage.Turkish] = "PARÇALAMA",
                    [AppLanguage.German] = "ZERSCHMETTERN",
                    [AppLanguage.Spanish] = "DESTROZO",
                },
                ["derby.levels.tier5"] = new()
                {
                    [AppLanguage.English] = "DEMOLISH",
                    [AppLanguage.Polish] = "ZNISZCZENIE",
                    [AppLanguage.Turkish] = "YOK ETME",
                    [AppLanguage.German] = "VERNICHTUNG",
                    [AppLanguage.Spanish] = "DEMOLICIÓN",
                },
                ["hud.title"] = new()
                {
                    [AppLanguage.English] = "Forza-like HUD Settings",
                    [AppLanguage.Polish] = "Ustawienia HUD w stylu Forza",
                    [AppLanguage.Turkish] = "Forza Tarzı HUD Ayarları",
                    [AppLanguage.German] = "Forza-artige HUD-Einstellungen",
                    [AppLanguage.Spanish] = "Configuración del HUD estilo Forza",
                },
                ["hud.colors"] = new()
                {
                    [AppLanguage.English] = "HUD Colors",
                    [AppLanguage.Polish] = "Kolory HUD",
                    [AppLanguage.Turkish] = "HUD Renkleri",
                    [AppLanguage.German] = "HUD-Farben",
                    [AppLanguage.Spanish] = "Colores del HUD",
                },
                ["hud.scorecolors"] = new()
                {
                    [AppLanguage.English] = "Forza-like HUD Colors",
                    [AppLanguage.Polish] = "Kolory HUD w stylu Forza",
                    [AppLanguage.Turkish] = "Forza Tarzı HUD Renkleri",
                    [AppLanguage.German] = "Forza-artige HUD-Farben",
                    [AppLanguage.Spanish] = "Colores del HUD estilo Forza",
                },
                ["hud.scorecolors.subtitle"] = new()
                {
                    [AppLanguage.English] = "Colors by drift angle tier",
                    [AppLanguage.Polish] = "Kolory wg poziomu kąta driftu",
                    [AppLanguage.Turkish] = "Drift açısı seviyesine göre renkler",
                    [AppLanguage.German] = "Farben nach Driftwinkel-Stufe",
                    [AppLanguage.Spanish] = "Colores por nivel de ángulo de derrape",
                },
                ["hud.colorlevel"] = new()
                {
                    [AppLanguage.English] = "Color lvl. {0}: {1}",
                    [AppLanguage.Polish] = "Kolor poz. {0}: {1}",
                    [AppLanguage.Turkish] = "Renk sv. {0}: {1}",
                    [AppLanguage.German] = "Farbe Stufe {0}: {1}",
                    [AppLanguage.Spanish] = "Color niv. {0}: {1}",
                },
                ["hud.scorecolor.default"] = new()
                {
                    [AppLanguage.English] = "Default / other",
                    [AppLanguage.Polish] = "Domyślny / inne",
                    [AppLanguage.Turkish] = "Varsayılan / diğer",
                    [AppLanguage.German] = "Standard / sonstige",
                    [AppLanguage.Spanish] = "Predeterminado / otro",
                },
                ["hud.scorecolor.idle"] = new()
                {
                    [AppLanguage.English] = "Idle (no activity)",
                    [AppLanguage.Polish] = "Bezczynność (brak aktywności)",
                    [AppLanguage.Turkish] = "Boşta (etkinlik yok)",
                    [AppLanguage.German] = "Leerlauf (keine Aktivität)",
                    [AppLanguage.Spanish] = "Inactivo (sin actividad)",
                },
                ["hud.scorecolor.backward"] = new()
                {
                    [AppLanguage.English] = "Backward drift",
                    [AppLanguage.Polish] = "Drift tyłem",
                    [AppLanguage.Turkish] = "Geri drift",
                    [AppLanguage.German] = "Rückwärtsdrift",
                    [AppLanguage.Spanish] = "Derrape hacia atrás",
                },
                ["hud.advanced_outgauge"] = new()
                {
                    [AppLanguage.English] = "Advanced OutGauge Activities",
                    [AppLanguage.Polish] = "Zaawansowane aktywności z OutGauge",
                    [AppLanguage.Turkish] = "Gelişmiş OutGauge Etkinlikleri",
                    [AppLanguage.German] = "Erweiterte OutGauge-Aktivitäten",
                    [AppLanguage.Spanish] = "Actividades avanzadas de OutGauge",
                },

                ["hud.advanced_outgauge.subtitle"] = new()
                {
                    [AppLanguage.English] = "Turns off burnout detection and its bonuses (360° Spin, Stationary Burnout, transitions).",
                    [AppLanguage.Polish] = "Wyłącza wykrywanie burnoutu i jego bonusy (360° Spin, burnout w miejscu, przejścia).",
                    [AppLanguage.Turkish] = "Lastik yakma tespitini ve bonuslarını kapatır (360° Dönüş, sabit lastik yakma, geçişler).",
                    [AppLanguage.German] = "Deaktiviert die Burnout-Erkennung und ihre Boni (360°-Dreh, stehender Burnout, Übergänge).",
                    [AppLanguage.Spanish] = "Desactiva la detección de quema de neumáticos y sus bonos (giro de 360°, quema estática, transiciones).",
                },
                ["hud.show"] = new()
                {
                    [AppLanguage.English] = "Show ingame HUD (IS_BTN)",
                    [AppLanguage.Polish] = "Pokaż HUD w grze (IS_BTN)",
                    [AppLanguage.Turkish] = "Oyun içi HUD'u göster (IS_BTN)",
                    [AppLanguage.German] = "Ingame-HUD anzeigen (IS_BTN)",
                    [AppLanguage.Spanish] = "Mostrar HUD en el juego (IS_BTN)",
                },
                ["hud.show.subtitle"] = new()
                {
                    [AppLanguage.English] = "Uses LFS's native IS_BTN buttons instead — works even without a transparent overlay window, but only supports 10 fixed colors.",
                    [AppLanguage.Polish] = "Używa natywnych przycisków IS_BTN z LFS zamiast nakładki — działa nawet bez przezroczystego okna, ale obsługuje tylko 10 stałych kolorów.",
                    [AppLanguage.Turkish] = "Bunun yerine LFS'in yerel IS_BTN düğmelerini kullanır — şeffaf katman olmadan da çalışır, ancak yalnızca 10 sabit rengi destekler.",
                    [AppLanguage.German] = "Verwendet stattdessen die nativen IS_BTN-Schaltflächen von LFS — funktioniert auch ohne transparentes Overlay-Fenster, unterstützt aber nur 10 feste Farben.",
                    [AppLanguage.Spanish] = "Usa los botones nativos IS_BTN de LFS en su lugar: funciona incluso sin una ventana de superposición transparente, pero solo admite 10 colores fijos.",
                },
                ["hud.show-disabled"] = new()
                {
                    [AppLanguage.English] = "Currently unavailable",
                    [AppLanguage.Polish] = "Obecnie niedostępne",
                    [AppLanguage.Turkish] = "Şu anda kullanılamıyor",
                    [AppLanguage.German] = "Derzeit nicht verfügbar",
                    [AppLanguage.Spanish] = "Actualmente no disponible",
                },
                ["hud.REVLimitter"] = new()
                {
                    [AppLanguage.English] = "Show ingame REV Limiter HUD",
                    [AppLanguage.Polish] = "Pokaż REV Limiter HUD w grze",
                    [AppLanguage.Turkish] = "Oyun içi REV Limiter HUD'unu göster",
                    [AppLanguage.German] = "Ingame-Drehzahlbegrenzer-HUD anzeigen",
                    [AppLanguage.Spanish] = "Mostrar HUD del limitador de RPM en el juego",
                },
                ["hud.REVLimitter.subtitle"] = new()
                {
                    [AppLanguage.English] = "Shows the current RPM in the bottom-left corner while the rev limiter is cutting ignition.",
                    [AppLanguage.Polish] = "Pokazuje aktualne obroty silnika w lewym dolnym rogu podczas cięcia zapłonu przez ogranicznik obrotów.",
                    [AppLanguage.Turkish] = "Devir sınırlayıcı ateşlemeyi keserken mevcut RPM'i sol alt köşede gösterir.",
                    [AppLanguage.German] = "Zeigt die aktuelle Drehzahl unten links an, während der Drehzahlbegrenzer die Zündung unterbricht.",
                    [AppLanguage.Spanish] = "Muestra las RPM actuales en la esquina inferior izquierda mientras el limitador de RPM corta el encendido.",
                },
                ["hud.master"] = new()
                {
                    [AppLanguage.English] = "Show HUD",
                    [AppLanguage.Polish] = "Pokaż HUD",
                    [AppLanguage.Turkish] = "HUD'u göster",
                    [AppLanguage.German] = "HUD anzeigen",
                    [AppLanguage.Spanish] = "Mostrar HUD",
                },
                ["hud.livepreview"] = new()
                {
                    [AppLanguage.English] = "Preview",
                    [AppLanguage.Polish] = "Podgląd",
                    [AppLanguage.Turkish] = "Canlı",
                    [AppLanguage.German] = "Vorschau",
                    [AppLanguage.Spanish] = "Vista previa",
                },
                ["rev.title"] = new()
                {
                    [AppLanguage.English] = "Rev Limiter",
                    [AppLanguage.Polish] = "Ogranicznik obrotów",
                    [AppLanguage.Turkish] = "Devir Sınırlayıcı",
                    [AppLanguage.German] = "Drehzahlbegrenzer",
                    [AppLanguage.Spanish] = "Limitador de RPM",
                },

                ["rev.vehicle"] = new()
                {
                    [AppLanguage.English] = "Vehicle: {0}",
                    [AppLanguage.Polish] = "Pojazd: {0}",
                    [AppLanguage.Turkish] = "Araç: {0}",
                    [AppLanguage.German] = "Fahrzeug: {0}",
                    [AppLanguage.Spanish] = "Vehículo: {0}",
                },
                ["rev.calibratehint"] = new()
                {
                    [AppLanguage.English] = "Bind rev limiter functions",
                    [AppLanguage.Polish] = "Przypisz funkcje limitera obrotów",
                    [AppLanguage.Turkish] = "Devir sınırlayıcı fonksiyonlarını ata",
                    [AppLanguage.German] = "Drehzahlbegrenzer-Funktionen zuweisen",
                    [AppLanguage.Spanish] = "Asignar funciones del limitador de RPM",
                },
                ["rev.autocalibrate"] = new()
                {
                    [AppLanguage.English] = "Auto-calibrate new vehicle",
                    [AppLanguage.Polish] = "Auto-kalibracja nowego pojazdu",
                    [AppLanguage.Turkish] = "Yeni aracı otomatik kalibre et",
                    [AppLanguage.German] = "Neues Fahrzeug automatisch kalibrieren",
                    [AppLanguage.Spanish] = "Autocalibrar vehículo nuevo",
                },
                ["rev.autocalibrate.subtitle"] = new()
                {
                    [AppLanguage.English] = "Shows the calibration prompt the first time you drive an unknown/uncalibrated car.",
                    [AppLanguage.Polish] = "Pokazuje okno kalibracji przy pierwszej jeździe nieznanym/niekalibrowanym pojazdem.",
                    [AppLanguage.Turkish] = "Bilinmeyen/kalibre edilmemiş bir arabayı ilk kez sürdüğünüzde kalibrasyon istemini gösterir.",
                    [AppLanguage.German] = "Zeigt den Kalibrierungsdialog beim ersten Fahren eines unbekannten/nicht kalibrierten Fahrzeugs.",
                    [AppLanguage.Spanish] = "Muestra el aviso de calibración la primera vez que conduces un coche desconocido/no calibrado.",
                },
                ["rev.calibrate.needs_outgauge"] = new()
                {
                    [AppLanguage.English] = "Can't calibrate — OutGauge is disconnected.",
                    [AppLanguage.Polish] = "Nie można skalibrować — OutGauge jest rozłączony.",
                    [AppLanguage.Turkish] = "Kalibre edilemiyor — OutGauge bağlı değil.",
                    [AppLanguage.German] = "Kalibrierung nicht möglich — OutGauge ist getrennt.",
                    [AppLanguage.Spanish] = "No se puede calibrar — OutGauge está desconectado.",
                },
                ["rev.calibrate"] = new()
                {
                    [AppLanguage.English] = "CALIBRATE",
                    [AppLanguage.Polish] = "KALIBRUJ",
                    [AppLanguage.Turkish] = "KALİBRE ET",
                    [AppLanguage.German] = "KALIBRIEREN",
                    [AppLanguage.Spanish] = "CALIBRAR",
                },
                ["rev.bindfunctions"] = new()
                {
                    [AppLanguage.English] = "BIND FUNCTIONS",
                    [AppLanguage.Polish] = "PRZYPISZ FUNKCJE",
                    [AppLanguage.Turkish] = "FONKSİYONLARI ATA",
                    [AppLanguage.German] = "FUNKTIONEN ZUWEISEN",
                    [AppLanguage.Spanish] = "ASIGNAR FUNCIONES",
                },
                ["rev.rpm"] = new()
                {
                    [AppLanguage.English] = "RPM:",
                    [AppLanguage.Polish] = "RPM:",
                    [AppLanguage.Turkish] = "RPM:",
                    [AppLanguage.German] = "U/min:",
                    [AppLanguage.Spanish] = "RPM:",
                },

                ["rev.limit"] = new()
                {
                    [AppLanguage.English] = "RPM limit: ",
                    [AppLanguage.Polish] = "Limit RPM: ",
                    [AppLanguage.Turkish] = "RPM limiti: ",
                    [AppLanguage.German] = "Drehzahlgrenze: ",
                    [AppLanguage.Spanish] = "Límite de RPM: ",
                },
                ["rev.cutms"] = new()
                {
                    [AppLanguage.English] = "Cut time [ms]:",
                    [AppLanguage.Polish] = "Czas cięcia [ms]:",
                    [AppLanguage.Turkish] = "Kesme süresi [ms]:",
                    [AppLanguage.German] = "Unterbrechungszeit [ms]:",
                    [AppLanguage.Spanish] = "Tiempo de corte [ms]:",
                },
                ["rev.calibrating"] = new()
                {
                    [AppLanguage.English] = "Calibration...",
                    [AppLanguage.Polish] = "Kalibracja...",
                    [AppLanguage.Turkish] = "Kalibrasyon...",
                    [AppLanguage.German] = "Kalibrierung...",
                    [AppLanguage.Spanish] = "Calibrando...",
                },
                ["rev.calibration_award"] = new()
                {
                    [AppLanguage.English] = "REV LIMITER CALIBRATION",
                    [AppLanguage.Polish] = "KALIBRACJA OGRANICZNIKA OBROTÓW",
                    [AppLanguage.Turkish] = "DEVİR SINIRLAYICI KALİBRASYONU",
                    [AppLanguage.German] = "DREHZAHLBEGRENZER-KALIBRIERUNG",
                    [AppLanguage.Spanish] = "CALIBRACIÓN DEL LIMITADOR DE RPM",
                },

                ["rev.bindings.title"] = new()
                {
                    [AppLanguage.English] = "Rev Limiter Bindings",
                    [AppLanguage.Polish] = "Przypisania ogranicznika obrotów",
                    [AppLanguage.Turkish] = "Devir Sınırlayıcı Kısayolları",
                    [AppLanguage.German] = "Drehzahlbegrenzer-Tastenbelegung",
                    [AppLanguage.Spanish] = "Asignaciones del limitador de RPM",
                },
                ["rev.bindings.toggle"] = new()
                {
                    [AppLanguage.English] = "Toggle On/Off",
                    [AppLanguage.Polish] = "Włącz/Wyłącz",
                    [AppLanguage.Turkish] = "Aç/Kapat",
                    [AppLanguage.German] = "Ein/Aus",
                    [AppLanguage.Spanish] = "Activar/Desactivar",
                },
                ["rev.bindings.calibrate"] = new()
                {
                    [AppLanguage.English] = "Calibrate",
                    [AppLanguage.Polish] = "Kalibruj",
                    [AppLanguage.Turkish] = "Kalibre Et",
                    [AppLanguage.German] = "Kalibrieren",
                    [AppLanguage.Spanish] = "Calibrar",
                },
                ["rev.bindings.decrease"] = new()
                {
                    [AppLanguage.English] = "Decrease Limit",
                    [AppLanguage.Polish] = "Zmniejsz limit",
                    [AppLanguage.Turkish] = "Limiti Azalt",
                    [AppLanguage.German] = "Grenze verringern",
                    [AppLanguage.Spanish] = "Disminuir límite",
                },
                ["rev.bindings.increase"] = new()
                {
                    [AppLanguage.English] = "Increase Limit",
                    [AppLanguage.Polish] = "Zwiększ limit",
                    [AppLanguage.Turkish] = "Limiti Artır",
                    [AppLanguage.German] = "Grenze erhöhen",
                    [AppLanguage.Spanish] = "Aumentar límite",
                },
                ["rev.bindings.unbound"] = new()
                {
                    [AppLanguage.English] = "Not bound",
                    [AppLanguage.Polish] = "Nie przypisano",
                    [AppLanguage.Turkish] = "Atanmadı",
                    [AppLanguage.German] = "Nicht zugewiesen",
                    [AppLanguage.Spanish] = "Sin asignar",
                },
                ["indicators.title"] = new()
                {
                    [AppLanguage.English] = "Turn Signals",
                    [AppLanguage.Polish] = "Kierunkowskazy",
                    [AppLanguage.Turkish] = "Sinyaller",
                    [AppLanguage.German] = "Blinker",
                    [AppLanguage.Spanish] = "Intermitentes",
                },
                ["indicators.soundscheck"] = new()
                {
                    [AppLanguage.English] = "Indicator sounds",
                    [AppLanguage.Polish] = "Dźwięki kierunkowskazów",
                    [AppLanguage.Turkish] = "Sinyal sesleri",
                    [AppLanguage.German] = "Blinkertöne",
                    [AppLanguage.Spanish] = "Sonidos de intermitentes",
                },
                ["indicators.soundscheck.subtitle"] = new()
                {
                    [AppLanguage.English] = "Plays a click sound when an indicator turns on/off and when it self-cancels.",
                    [AppLanguage.Polish] = "Odtwarza dźwięk kliknięcia przy włączeniu/wyłączeniu kierunkowskazu oraz przy jego automatycznym wyłączeniu.",
                    [AppLanguage.Turkish] = "Sinyal açılıp kapandığında ve kendiliğinden iptal olduğunda tıklama sesi çalar.",
                    [AppLanguage.German] = "Spielt ein Klickgeräusch ab, wenn ein Blinker ein-/ausgeschaltet wird oder sich selbst abschaltet.",
                    [AppLanguage.Spanish] = "Reproduce un sonido de clic al activar/desactivar un intermitente y cuando se cancela automáticamente.",
                },
                ["indicators.centeroff"] = new()
                {
                    [AppLanguage.English] = "Auto-cancel on wheel centering",
                    [AppLanguage.Polish] = "Auto-wyłączanie po centrowaniu kierownicy",
                    [AppLanguage.Turkish] = "Direksiyon ortalanınca otomatik kapat",
                    [AppLanguage.German] = "Automatisches Abschalten beim Zentrieren des Lenkrads",
                    [AppLanguage.Spanish] = "Desactivación automática al centrar el volante",
                },
                ["indicators.centeroff.subtitle"] = new()
                {
                    [AppLanguage.English] = "Automatically turns the indicator off once the steering wheel returns near center, like a real car.",
                    [AppLanguage.Polish] = "Automatycznie wyłącza kierunkowskaz, gdy kierownica wróci blisko pozycji środkowej, tak jak w prawdziwym aucie.",
                    [AppLanguage.Turkish] = "Direksiyon merkeze yakın bir konuma döndüğünde sinyali gerçek bir araçtaki gibi otomatik olarak kapatır.",
                    [AppLanguage.German] = "Schaltet den Blinker automatisch aus, sobald das Lenkrad wieder nahe der Mittelstellung ist, wie in einem echten Auto.",
                    [AppLanguage.Spanish] = "Apaga automáticamente el intermitente cuando el volante vuelve cerca del centro, como en un coche real.",
                },
                ["indicators.armthreshold"] = new()
                {
                    [AppLanguage.English] = "Minimum turn to arm:",
                    [AppLanguage.Polish] = "Minimalny skręt do uzbrojenia:",
                    [AppLanguage.Turkish] = "Etkinleştirme için minimum dönüş:",
                    [AppLanguage.German] = "Mindestlenkeinschlag zum Scharfstellen:",
                    [AppLanguage.Spanish] = "Giro mínimo para armar:",
                },
                ["indicators.armthreshold.subtitle"] = new()
                {
                    [AppLanguage.English] = "How far you need to turn the wheel before returning to center will cancel the indicator.",
                    [AppLanguage.Polish] = "Jak mocno trzeba skręcić kierownicą, zanim powrót do środka wyłączy kierunkowskaz.",
                    [AppLanguage.Turkish] = "Merkeze dönüşün sinyali iptal etmesi için direksiyonun ne kadar çevrilmesi gerektiği.",
                    [AppLanguage.German] = "Wie weit das Lenkrad gedreht werden muss, damit die Rückkehr zur Mitte den Blinker abschaltet.",
                    [AppLanguage.Spanish] = "Cuánto hay que girar el volante antes de que volver al centro cancele el intermitente.",
                },
                ["indicators.centerthreshold"] = new()
                {
                    [AppLanguage.English] = "Center threshold:",
                    [AppLanguage.Polish] = "Próg wycentrowania:",
                    [AppLanguage.Turkish] = "Merkez eşiği:",
                    [AppLanguage.German] = "Zentrierschwelle:",
                    [AppLanguage.Spanish] = "Umbral de centrado:",
                },
                ["indicators.centerthreshold.subtitle"] = new()
                {
                    [AppLanguage.English] = "How close to center (in steering %) the wheel must return before the indicator cancels.",
                    [AppLanguage.Polish] = "Jak blisko środka (w % skrętu kierownicy) trzeba wrócić, żeby kierunkowskaz się wyłączył.",
                    [AppLanguage.Turkish] = "Sinyalin iptal olması için direksiyonun merkeze ne kadar yakın dönmesi gerektiği (direksiyon % olarak).",
                    [AppLanguage.German] = "Wie nah am Zentrum (in Lenkwinkel-%) das Lenkrad zurückkehren muss, damit der Blinker abschaltet.",
                    [AppLanguage.Spanish] = "Qué tan cerca del centro (en % de giro) debe volver el volante para que el intermitente se cancele.",
                },
                ["indicators.master"] = new()
                {
                    [AppLanguage.English] = "Enable turn signals",
                    [AppLanguage.Polish] = "Włącz kierunkowskazy",
                    [AppLanguage.Turkish] = "Sinyalleri etkinleştir",
                    [AppLanguage.German] = "Blinker aktivieren",
                    [AppLanguage.Spanish] = "Activar intermitentes",
                },
                ["indicators.wheelsetup"] = new()
                {
                    [AppLanguage.English] = "🎮 Configure Steering Wheel",
                    [AppLanguage.Polish] = "🎮 Skonfiguruj kierownicę",
                    [AppLanguage.Turkish] = "🎮 Direksiyonu Yapılandır",
                    [AppLanguage.German] = "🎮 Lenkrad konfigurieren",
                    [AppLanguage.Spanish] = "🎮 Configurar volante",
                },
                ["indicators.group.status"] = new()
                {
                    [AppLanguage.English] = "STATUS",
                    [AppLanguage.Polish] = "STATUS",
                    [AppLanguage.Turkish] = "DURUM",
                    [AppLanguage.German] = "STATUS",
                    [AppLanguage.Spanish] = "ESTADO",
                },
                ["indicators.group.wheel"] = new()
                {
                    [AppLanguage.English] = "WHEEL & AUTO-CANCEL",
                    [AppLanguage.Polish] = "KIEROWNICA I AUTO-WYŁĄCZANIE",
                    [AppLanguage.Turkish] = "DİREKSİYON VE OTOMATİK İPTAL",
                    [AppLanguage.German] = "LENKRAD & AUTO-ABSCHALTUNG",
                    [AppLanguage.Spanish] = "VOLANTE Y AUTOCANCELACIÓN",
                },
                ["indicators.group.sounds"] = new()
                {
                    [AppLanguage.English] = "SOUNDS",
                    [AppLanguage.Polish] = "DŹWIĘKI",
                    [AppLanguage.Turkish] = "SESLER",
                    [AppLanguage.German] = "TÖNE",
                    [AppLanguage.Spanish] = "SONIDOS",
                },
                ["indicators.volume"] = new()
                {
                    [AppLanguage.English] = "Volume",
                    [AppLanguage.Polish] = "Głośność",
                    [AppLanguage.Turkish] = "Ses düzeyi",
                    [AppLanguage.German] = "Lautstärke",
                    [AppLanguage.Spanish] = "Volumen",
                },
                ["indicators.lights"] = new()
                {
                    [AppLanguage.English] = "LIGHTS",
                    [AppLanguage.Polish] = "ŚWIATŁA",
                    [AppLanguage.Turkish] = "FARLAR",
                    [AppLanguage.German] = "LICHTER",
                    [AppLanguage.Spanish] = "LUCES",
                },
                ["indicators.dash.subtitle"] = new()
                {
                    [AppLanguage.English] = "Live from your car's dashboard (OutGauge): turn signals and high beam.",
                    [AppLanguage.Polish] = "Na żywo z deski rozdzielczej auta (OutGauge): kierunkowskazy i światła długie.",
                    [AppLanguage.Turkish] = "Aracın gösterge panelinden canlı (OutGauge): sinyaller ve far.",
                    [AppLanguage.German] = "Live vom Armaturenbrett deines Autos (OutGauge): Blinker und Fernlicht.",
                    [AppLanguage.Spanish] = "En vivo desde el salpicadero del coche (OutGauge): intermitentes y luces largas.",
                },
                ["indicators.keybind"] = new()
                {
                    [AppLanguage.English] = "Key bind:",
                    [AppLanguage.Polish] = "Przypisanie klawisza:",
                    [AppLanguage.Turkish] = "Tuş ataması:",
                    [AppLanguage.German] = "Tastenzuweisung:",
                    [AppLanguage.Spanish] = "Asignación de tecla:",
                },
                ["indicators.left"] = new()
                {
                    [AppLanguage.English] = "LEFT",
                    [AppLanguage.Polish] = "LEWY",
                    [AppLanguage.Turkish] = "SOL",
                    [AppLanguage.German] = "LINKS",
                    [AppLanguage.Spanish] = "IZQUIERDA",
                },
                ["indicators.right"] = new()
                {
                    [AppLanguage.English] = "RIGHT",
                    [AppLanguage.Polish] = "PRAWY",
                    [AppLanguage.Turkish] = "SAĞ",
                    [AppLanguage.German] = "RECHTS",
                    [AppLanguage.Spanish] = "DERECHA",
                },
                ["indicators.hazard"] = new()
                {
                    [AppLanguage.English] = "HAZARD",
                    [AppLanguage.Polish] = "AWARYJNE",
                    [AppLanguage.Turkish] = "DÖRTLÜ FLAŞÖR",
                    [AppLanguage.German] = "WARNBLINKER",
                    [AppLanguage.Spanish] = "EMERGENCIA",
                },
                ["indicators.currentstatus"] = new()
                {
                    [AppLanguage.English] = "Current status:",
                    [AppLanguage.Polish] = "Aktualny stan:",
                    [AppLanguage.Turkish] = "Mevcut durum:",
                    [AppLanguage.German] = "Aktueller Status:",
                    [AppLanguage.Spanish] = "Estado actual:",
                },
                ["indicators.steering"] = new()
                {
                    [AppLanguage.English] = "STEERING",
                    [AppLanguage.Polish] = "KIEROWNICA",
                    [AppLanguage.Turkish] = "DİREKSİYON",
                    [AppLanguage.German] = "LENKUNG",
                    [AppLanguage.Spanish] = "DIRECCIÓN",
                },
                ["indicators.signal"] = new()
                {
                    [AppLanguage.English] = "SIGNAL",
                    [AppLanguage.Polish] = "SYGNAŁ",
                    [AppLanguage.Turkish] = "SİNYAL",
                    [AppLanguage.German] = "SIGNAL",
                    [AppLanguage.Spanish] = "SEÑAL",
                },
                ["keybind.dialog.title"] = new()
                {
                    [AppLanguage.English] = "Key Binding",
                    [AppLanguage.Polish] = "Przypisanie klawisza",
                    [AppLanguage.Turkish] = "Tuş Atama",
                    [AppLanguage.German] = "Tastenzuweisung",
                    [AppLanguage.Spanish] = "Asignación de tecla",
                },
                ["keybind.dialog.press"] = new()
                {
                    [AppLanguage.English] = "Press a key for: {0}\n\n(ESC to cancel, DELETE to unbind)",
                    [AppLanguage.Polish] = "Naciśnij klawisz dla: {0}\n\n(ESC anuluje, DELETE usuwa przypisanie)",
                    [AppLanguage.Turkish] = "{0} için bir tuşa basın\n\n(İptal: ESC, kaldırmak için: DELETE)",
                    [AppLanguage.German] = "Drücke eine Taste für: {0}\n\n(ESC zum Abbrechen, ENTF zum Entfernen)",
                    [AppLanguage.Spanish] = "Presiona una tecla para: {0}\n\n(ESC cancela, SUPR quita la asignación)",
                },
                ["keybind.dialog.bound"] = new()
                {
                    [AppLanguage.English] = "Bound: {0}\n\nClick to return...",
                    [AppLanguage.Polish] = "Przypisano: {0}\n\nKliknij, aby wrócić...",
                    [AppLanguage.Turkish] = "Atandı: {0}\n\nDönmek için tıklayın...",
                    [AppLanguage.German] = "Zugewiesen: {0}\n\nKlicken zum Zurückkehren...",
                    [AppLanguage.Spanish] = "Asignado: {0}\n\nHaz clic para volver...",
                },
                ["keybind.dialog.unbound"] = new()
                {
                    [AppLanguage.English] = "Binding removed",
                    [AppLanguage.Polish] = "Przypisanie usunięte",
                    [AppLanguage.Turkish] = "Atama kaldırıldı",
                    [AppLanguage.German] = "Zuweisung entfernt",
                    [AppLanguage.Spanish] = "Asignación eliminada",
                },
                ["status.keybound"] = new()
                {
                    [AppLanguage.English] = "Key '{0}' changed to: {1}",
                    [AppLanguage.Polish] = "Klawisz '{0}' zmieniony na: {1}",
                    [AppLanguage.Turkish] = "'{0}' tuşu şununla değiştirildi: {1}",
                    [AppLanguage.German] = "Taste '{0}' geändert zu: {1}",
                    [AppLanguage.Spanish] = "Tecla '{0}' cambiada a: {1}",
                },

                ["drift.label.fast1"] = new()
                {
                    [AppLanguage.English] = "FAST DRIVING",
                    [AppLanguage.Polish] = "SZYBKA JAZDA",
                    [AppLanguage.Turkish] = "HIZLI SÜRÜŞ",
                    [AppLanguage.German] = "SCHNELLES FAHREN",
                    [AppLanguage.Spanish] = "CONDUCCIÓN RÁPIDA",
                },
                ["drift.label.fast2"] = new()
                {
                    [AppLanguage.English] = "INSANE DRIVING!",
                    [AppLanguage.Polish] = "POJEBANA JAZDA!",
                    [AppLanguage.Turkish] = "ÇILGIN SÜRÜŞ!",
                    [AppLanguage.German] = "WAHNSINNIGES FAHREN!",
                    [AppLanguage.Spanish] = "¡CONDUCCIÓN INSANA!",
                },
                ["drift.label.fast3"] = new()
                {
                    [AppLanguage.English] = "MENTAL DRIVING!",
                    [AppLanguage.Polish] = "POKURWIONA JAZDA!",
                    [AppLanguage.Turkish] = "DELİ SÜRÜŞ!",
                    [AppLanguage.German] = "IRRES FAHREN!",
                    [AppLanguage.Spanish] = "¡CONDUCCIÓN DEMENCIAL!",
                },
                ["drift.label.fast4"] = new()
                {
                    [AppLanguage.English] = "EXTREME DRIVING!",
                    [AppLanguage.Polish] = "PODKURWIONA JAZDA!",
                    [AppLanguage.Turkish] = "AŞIRI SÜRÜŞ!",
                    [AppLanguage.German] = "EXTREMES FAHREN!",
                    [AppLanguage.Spanish] = "¡CONDUCCIÓN EXTREMA!",
                },
                ["drift.label.fast5"] = new()
                {
                    [AppLanguage.English] = "GODLIKE DRIVING!",
                    [AppLanguage.Polish] = "BOSKA JAZDA!",
                    [AppLanguage.Turkish] = "İLAHİ SÜRÜŞ!",
                    [AppLanguage.German] = "GÖTTLICHES FAHREN!",
                    [AppLanguage.Spanish] = "¡CONDUCCIÓN DIVINA!",
                },
                ["drift.label.angle_extreme"] = new()
                {
                    [AppLanguage.English] = "INSANE DRIFT",
                    [AppLanguage.Polish] = "DOSPERMIONY DRIFT",
                    [AppLanguage.Turkish] = "ÇILGIN DRİFT",
                    [AppLanguage.German] = "WAHNSINNIGER DRIFT",
                    [AppLanguage.Spanish] = "DERRAPE INSANO",
                },
                ["drift.label.angle_ultraextreme"] = new()
                {
                    [AppLanguage.English] = "EXTREME DRIFT",
                    [AppLanguage.Polish] = "DOKURWIONY DRIFT",
                    [AppLanguage.Turkish] = "AŞIRI DRİFT",
                    [AppLanguage.German] = "EXTREMER DRIFT",
                    [AppLanguage.Spanish] = "DERRAPE EXTREMO",
                },
                ["drift.label.angle_backward"] = new()
                {
                    [AppLanguage.English] = "BACKWARD DRIFT",
                    [AppLanguage.Polish] = "DRIFT TYŁEM",
                    [AppLanguage.Turkish] = "GERİYE DRİFT",
                    [AppLanguage.German] = "RÜCKWÄRTSDRIFT",
                    [AppLanguage.Spanish] = "DERRAPE HACIA ATRÁS",
                },
                ["drift.label.angle_high"] = new()
                {
                    [AppLanguage.English] = "AWESOME DRIFT",
                    [AppLanguage.Polish] = "DOJEBANY DRIFT",
                    [AppLanguage.Turkish] = "HARİKA DRİFT",
                    [AppLanguage.German] = "GENIALER DRIFT",
                    [AppLanguage.Spanish] = "DERRAPE IMPRESIONANTE",
                },
                ["drift.label.angle_good"] = new()
                {
                    [AppLanguage.English] = "GOOD DRIFT",
                    [AppLanguage.Polish] = "DOBRY DRIFT",
                    [AppLanguage.Turkish] = "İYİ DRİFT",
                    [AppLanguage.German] = "GUTER DRIFT",
                    [AppLanguage.Spanish] = "BUEN DERRAPE",
                },
                ["drift.label.angle_extremee"] = new()
                {
                    [AppLanguage.English] = "INSANE E-BRAKE DRIFT",
                    [AppLanguage.Polish] = "DOSPERMIONY E-BRAKE DRIFT",
                    [AppLanguage.Turkish] = "ÇILGIN EL FRENİ DRİFTİ",
                    [AppLanguage.German] = "WAHNSINNIGER HANDBREMSDRIFT",
                    [AppLanguage.Spanish] = "DERRAPE INSANO CON FRENO DE MANO",
                },
                ["drift.label.angle_ultraextremee"] = new()
                {
                    [AppLanguage.English] = "EXTREME E-BRAKE DRIFT",
                    [AppLanguage.Polish] = "DOKURWIONY E-BRAKE DRIFT",
                    [AppLanguage.Turkish] = "AŞIRI EL FRENİ DRİFTİ",
                    [AppLanguage.German] = "EXTREMER HANDBREMSDRIFT",
                    [AppLanguage.Spanish] = "DERRAPE EXTREMO CON FRENO DE MANO",
                },
                ["drift.label.angle_backwarde"] = new()
                {
                    [AppLanguage.English] = "BACKWARD E-BRAKE DRIFT",
                    [AppLanguage.Polish] = "DRIFT E-BRAKE TYŁEM",
                    [AppLanguage.Turkish] = "GERİYE EL FRENİ DRİFTİ",
                    [AppLanguage.German] = "RÜCKWÄRTS-HANDBREMSDRIFT",
                    [AppLanguage.Spanish] = "DERRAPE HACIA ATRÁS CON FRENO DE MANO",
                },
                ["drift.label.angle_highe"] = new()
                {
                    [AppLanguage.English] = "AWESOME E-BRAKE DRIFT",
                    [AppLanguage.Polish] = "DOJEBANY E-BRAKE DRIFT",
                    [AppLanguage.Turkish] = "HARİKA EL FRENİ DRİFTİ",
                    [AppLanguage.German] = "GENIALER HANDBREMSDRIFT",
                    [AppLanguage.Spanish] = "DERRAPE IMPRESIONANTE CON FRENO DE MANO",
                },
                ["drift.label.angle_goode"] = new()
                {
                    [AppLanguage.English] = "GOOD E-BRAKE DRIFT",
                    [AppLanguage.Polish] = "DOBRY E-BRAKE DRIFT",
                    [AppLanguage.Turkish] = "İYİ EL FRENİ DRİFTİ",
                    [AppLanguage.German] = "GUTER HANDBREMSDRIFT",
                    [AppLanguage.Spanish] = "BUEN DERRAPE CON FRENO DE MANO",
                },
                ["drift.label.fast_drift"] = new()
                {
                    [AppLanguage.English] = "FAST DRIFT",
                    [AppLanguage.Polish] = "SZYBKI DRIFT",
                    [AppLanguage.Turkish] = "HIZLI DRİFT",
                    [AppLanguage.German] = "SCHNELLER DRIFT",
                    [AppLanguage.Spanish] = "DERRAPE RÁPIDO",
                },
                ["drift.label.transition_drift"] = new()
                {
                    [AppLanguage.English] = "TRANSITION!",
                    [AppLanguage.Polish] = "PRZEKŁADKA!",
                    [AppLanguage.Turkish] = "GEÇİŞ!",
                    [AppLanguage.German] = "ÜBERGANG!",
                    [AppLanguage.Spanish] = "¡TRANSICIÓN!",
                },
                ["drift.label.long_drift"] = new()
                {
                    [AppLanguage.English] = "LONG DRIFT!",
                    [AppLanguage.Polish] = "DŁUGI POŚLIZG!",
                    [AppLanguage.Turkish] = "UZUN DRİFT!",
                    [AppLanguage.German] = "LANGER DRIFT!",
                    [AppLanguage.Spanish] = "¡DERRAPE LARGO!",
                },
                ["drift.label.deep_long_drift"] = new()
                {
                    [AppLanguage.English] = "DEEP ANGLE DRIFT!",
                    [AppLanguage.Polish] = "GŁĘBOKI KĄT DRIFTU!",
                    [AppLanguage.Turkish] = "DERİN AÇILI DRİFT!",
                    [AppLanguage.German] = "DRIFT MIT TIEFEM WINKEL!",
                    [AppLanguage.Spanish] = "¡DERRAPE DE ÁNGULO PROFUNDO!",
                },
                ["drift.label.generic"] = new()
                {
                    [AppLanguage.English] = "DRIFT",
                    [AppLanguage.Polish] = "DRIFT",
                    [AppLanguage.Turkish] = "DRİFT",
                    [AppLanguage.German] = "DRIFT",
                    [AppLanguage.Spanish] = "DERRAPE",
                },
                ["drift.label.generice"] = new()
                {
                    [AppLanguage.English] = "E-BRAKE DRIFT",
                    [AppLanguage.Polish] = "E-BRAKE DRIFT",
                    [AppLanguage.Turkish] = "E-BRAKE DRİFT",
                    [AppLanguage.German] = "HANDBREMSDRIFT",
                    [AppLanguage.Spanish] = "DERRAPE CON FRENO DE MANO",
                },
                ["drift.label.burnout_good"] = new()
                {
                    [AppLanguage.English] = "BURNOUT",
                    [AppLanguage.Polish] = "BURNOUT",
                    [AppLanguage.Turkish] = "LASTİK YAKMA",
                    [AppLanguage.German] = "BURNOUT",
                    [AppLanguage.Spanish] = "QUEMA DE NEUMÁTICOS",
                },
                ["drift.label.burnout_high"] = new()
                {
                    [AppLanguage.English] = "GREAT BURNOUT!",
                    [AppLanguage.Polish] = "DOBRY BURNOUT!",
                    [AppLanguage.Turkish] = "HARİKA LASTİK YAKMA!",
                    [AppLanguage.German] = "STARKER BURNOUT!",
                    [AppLanguage.Spanish] = "¡GRAN QUEMA DE NEUMÁTICOS!",
                },
                ["drift.label.burnout_extreme"] = new()
                {
                    [AppLanguage.English] = "EXTREME BURNOUT!",
                    [AppLanguage.Polish] = "DOKURWIONY BURNOUT!",
                    [AppLanguage.Turkish] = "AŞIRI LASTİK YAKMA!",
                    [AppLanguage.German] = "EXTREMER BURNOUT!",
                    [AppLanguage.Spanish] = "¡QUEMA DE NEUMÁTICOS EXTREMA!",
                },
                ["drift.label.burnout_insane"] = new()
                {
                    [AppLanguage.English] = "INSANE BURNOUT!",
                    [AppLanguage.Polish] = "DOSPERMIONY BURNOUT!",
                    [AppLanguage.Turkish] = "ÇILGIN LASTİK YAKMA!",
                    [AppLanguage.German] = "WAHNSINNIGER BURNOUT!",
                    [AppLanguage.Spanish] = "¡QUEMA DE NEUMÁTICOS INSANA!",
                },
                ["drift.label.burnout_spin"] = new()
                {
                    [AppLanguage.English] = "360° SPIN!",
                    [AppLanguage.Polish] = "BĄCZEK 360°!",
                    [AppLanguage.Turkish] = "360° DÖNÜŞ!",
                    [AppLanguage.German] = "360°-DREH!",
                    [AppLanguage.Spanish] = "¡GIRO DE 360°!",
                },
                ["bonus.stationary_burnout"] = new()
                {
                    [AppLanguage.English] = "STATIONARY BURNOUT",
                    [AppLanguage.Polish] = "BURNOUT W MIEJSCU",
                    [AppLanguage.Turkish] = "SABİT BURNOUT",
                    [AppLanguage.German] = "STEHENDER BURNOUT",
                    [AppLanguage.Spanish] = "BURNOUT ESTÁTICO",
                },
                ["bonus.donut"] = new()
                {
                    [AppLanguage.English] = "DONUT!",
                    [AppLanguage.Polish] = "DONUT!",
                    [AppLanguage.Turkish] = "DONUT!",
                    [AppLanguage.German] = "DONUT!",
                    [AppLanguage.Spanish] = "¡DONUT!",
                },
                ["bonus.power_spin"] = new()
                {
                    [AppLanguage.English] = "POWER SPIN!",
                    [AppLanguage.Polish] = "MOCNY OBRÓT!",
                    [AppLanguage.Turkish] = "GÜÇLÜ DÖNÜŞ!",
                    [AppLanguage.German] = "POWER SPIN!",
                    [AppLanguage.Spanish] = "¡GIRO POTENTE!",
                },
                ["bonus.unstoppable"] = new()
                {
                    [AppLanguage.English] = "UNSTOPPABLE!",
                    [AppLanguage.Polish] = "NIEPOWSTRZYMANY!",
                    [AppLanguage.Turkish] = "DURDURULAMAZ!",
                    [AppLanguage.German] = "UNAUFHALTSAM!",
                    [AppLanguage.Spanish] = "¡IMPARABLE!",
                },
                ["bonus.clean_lap"] = new()
                {
                    [AppLanguage.English] = "CLEAN LAP!",
                    [AppLanguage.Polish] = "CZYSTE OKRĄŻENIE!",
                    [AppLanguage.Turkish] = "TEMİZ TUR!",
                    [AppLanguage.German] = "SAUBERE RUNDE!",
                    [AppLanguage.Spanish] = "¡VUELTA LIMPIA!",
                },
                ["bonus.burnout_to_drift"] = new()
                {
                    [AppLanguage.English] = "BURNOUT → DRIFT!",
                    [AppLanguage.Polish] = "BURNOUT → DRIFT!",
                    [AppLanguage.Turkish] = "BURNOUT → DRIFT!",
                    [AppLanguage.German] = "BURNOUT → DRIFT!",
                    [AppLanguage.Spanish] = "BURNOUT → DRIFT!",
                },
                ["bonus.drift_to_burnout"] = new()
                {
                    [AppLanguage.English] = "DRIFT → BURNOUT!",
                    [AppLanguage.Polish] = "DRIFT → BURNOUT!",
                    [AppLanguage.Turkish] = "DRIFT → BURNOUT!",
                    [AppLanguage.German] = "DRIFT → BURNOUT!",
                    [AppLanguage.Spanish] = "DRIFT → BURNOUT!",
                },
                ["drift.label.entry_good"] = new()
                {
                    [AppLanguage.English] = "GOOD ENTRY",
                    [AppLanguage.Polish] = "DOBRE WEJŚCIE",
                    [AppLanguage.Turkish] = "İYİ GİRİŞ",
                    [AppLanguage.German] = "GUTER EINSTIEG",
                    [AppLanguage.Spanish] = "BUENA ENTRADA",
                },
                ["drift.label.entry_high"] = new()
                {
                    [AppLanguage.English] = "HIGH ENTRY",
                    [AppLanguage.Polish] = "WYSOKIE WEJŚCIE",
                    [AppLanguage.Turkish] = "YÜKSEK GİRİŞ",
                    [AppLanguage.German] = "HOHER EINSTIEG",
                    [AppLanguage.Spanish] = "ENTRADA ALTA",
                },
                ["drift.label.entry_extreme"] = new()
                {
                    [AppLanguage.English] = "EXTREME ENTRY",
                    [AppLanguage.Polish] = "EKSTREMALNE WEJŚCIE",
                    [AppLanguage.Turkish] = "AŞIRI GİRİŞ",
                    [AppLanguage.German] = "EXTREMER EINSTIEG",
                    [AppLanguage.Spanish] = "ENTRADA EXTREMA",
                },
                ["drift.label.entry_ultraextreme"] = new()
                {
                    [AppLanguage.English] = "ULTRA EXTREME ENTRY",
                    [AppLanguage.Polish] = "ULTRA EKSTREMALNE WEJŚCIE",
                    [AppLanguage.Turkish] = "ULTRA AŞIRI GİRİŞ",
                    [AppLanguage.German] = "ULTRA-EXTREMER EINSTIEG",
                    [AppLanguage.Spanish] = "ENTRADA ULTRA EXTREMA",
                },
                ["drift.label.entry_backward"] = new()
                {
                    [AppLanguage.English] = "BACKWARD ENTRY",
                    [AppLanguage.Polish] = "WEJŚCIE TYŁEM",
                    [AppLanguage.Turkish] = "GERİYE GİRİŞ",
                    [AppLanguage.German] = "RÜCKWÄRTS-EINSTIEG",
                    [AppLanguage.Spanish] = "ENTRADA HACIA ATRÁS",
                },
                ["wheelconfig.title"] = new()
                {
                    [AppLanguage.English] = "Wheel Configuration",
                    [AppLanguage.Polish] = "Konfiguracja kierownicy",
                    [AppLanguage.Turkish] = "Direksiyon Yapılandırması",
                    [AppLanguage.German] = "Lenkrad-Konfiguration",
                    [AppLanguage.Spanish] = "Configuración del volante",
                },
                ["wheelconfig.nodetectauto"] = new()
                {
                    [AppLanguage.English] = "Wheel not detected automatically.\nSelect a device from the list:",
                    [AppLanguage.Polish] = "Nie znaleziono kierownicy automatycznie.\nWybierz urządzenie z listy:",
                    [AppLanguage.Turkish] = "Direksiyon otomatik olarak algılanamadı.\nListeden bir cihaz seçin:",
                    [AppLanguage.German] = "Lenkrad nicht automatisch erkannt.\nWähle ein Gerät aus der Liste:",
                    [AppLanguage.Spanish] = "El volante no se detectó automáticamente.\nSelecciona un dispositivo de la lista:",
                },
                ["wheelconfig.nodetect"] = new()
                {
                    [AppLanguage.English] = "No game controllers detected.\nConnect your wheel and reopen this dialog.",
                    [AppLanguage.Polish] = "Nie wykryto żadnych kontrolerów gier.\nPodłącz kierownicę i otwórz ten dialog ponownie.",
                    [AppLanguage.Turkish] = "Herhangi bir oyun kontrolörü algılanmadı.\nDireksiyonunuzu bağlayıp bu pencereyi tekrar açın.",
                    [AppLanguage.German] = "Keine Spielcontroller erkannt.\nSchließe dein Lenkrad an und öffne diesen Dialog erneut.",
                    [AppLanguage.Spanish] = "No se detectaron controladores de juego.\nConecta tu volante y vuelve a abrir este diálogo.",
                },
                ["wheelconfig.abort"] = new()
                {
                    [AppLanguage.English] = "Cancel",
                    [AppLanguage.Polish] = "Anuluj",
                    [AppLanguage.Turkish] = "İptal",
                    [AppLanguage.German] = "Abbrechen",
                    [AppLanguage.Spanish] = "Cancelar",
                },
                ["wheelconfig.calibrateinfo"] = new()
                {
                    [AppLanguage.English] = "Turn the wheel fully LEFT and RIGHT —\nthe program will detect the correct axis automatically.",
                    [AppLanguage.Polish] = "Skręć kierownicą maksymalnie w LEWO i w PRAWO —\nprogram wykryje właściwą oś automatycznie.",
                    [AppLanguage.Turkish] = "Direksiyonu tamamen SOLA ve SAĞA çevirin —\nprogram doğru ekseni otomatik olarak algılayacaktır.",
                    [AppLanguage.German] = "Drehe das Lenkrad ganz nach LINKS und RECHTS —\ndas Programm erkennt die richtige Achse automatisch.",
                    [AppLanguage.Spanish] = "Gira el volante completamente a la IZQUIERDA y a la DERECHA —\nel programa detectará el eje correcto automáticamente.",
                },
                ["wheelconfig.detection"] = new()
                {
                    [AppLanguage.English] = "Calibration: detecting movement... (3 s)",
                    [AppLanguage.Polish] = "Kalibracja: wykrywanie ruchu... (3 s)",
                    [AppLanguage.Turkish] = "Kalibrasyon: hareket algılanıyor... (3 sn)",
                    [AppLanguage.German] = "Kalibrierung: Bewegung wird erkannt... (3 s)",
                    [AppLanguage.Spanish] = "Calibración: detectando movimiento... (3 s)",
                },
                ["wheelconfig.nomotion"] = new()
                {
                    [AppLanguage.English] = "No motion detected — select the axis manually above.",
                    [AppLanguage.Polish] = "Nie wykryto ruchu — wybierz oś ręcznie powyżej.",
                    [AppLanguage.Turkish] = "Hareket algılanmadı — ekseni yukarıdan manuel olarak seçin.",
                    [AppLanguage.German] = "Keine Bewegung erkannt — wähle die Achse oben manuell aus.",
                    [AppLanguage.Spanish] = "No se detectó movimiento — selecciona el eje manualmente arriba.",
                },
                ["wheelconfig.manualset"] = new()
                {
                    [AppLanguage.English] = "Or select the axis manually:",
                    [AppLanguage.Polish] = "Albo wybierz oś ręcznie:",
                    [AppLanguage.Turkish] = "Ya da ekseni manuel olarak seçin:",
                    [AppLanguage.German] = "Oder wähle die Achse manuell aus:",
                    [AppLanguage.Spanish] = "O selecciona el eje manualmente:",
                },
                ["wheelconfig.otherdevice"] = new()
                {
                    [AppLanguage.English] = "← Other device",
                    [AppLanguage.Polish] = "← Inne urządzenie",
                    [AppLanguage.Turkish] = "← Diğer cihaz",
                    [AppLanguage.German] = "← Anderes Gerät",
                    [AppLanguage.Spanish] = "← Otro dispositivo",
                },
                ["wheelconfig.disconnect"] = new()
                {
                    [AppLanguage.English] = "The selected device is no longer connected.",
                    [AppLanguage.Polish] = "Wybrane urządzenie nie jest już podłączone.",
                    [AppLanguage.Turkish] = "Seçilen cihazın bağlantısı artık yok.",
                    [AppLanguage.German] = "Das ausgewählte Gerät ist nicht mehr verbunden.",
                    [AppLanguage.Spanish] = "El dispositivo seleccionado ya no está conectado.",
                },
                ["wheelconfig.axis"] = new()
                {
                    [AppLanguage.English] = "Steering axis set to: ",
                    [AppLanguage.Polish] = "Oś skrętu ustawiona na: ",
                    [AppLanguage.Turkish] = "Direksiyon ekseni şu şekilde ayarlandı: ",
                    [AppLanguage.German] = "Lenkachse eingestellt auf: ",
                    [AppLanguage.Spanish] = "Eje de dirección establecido en: ",
                },
                ["wheelconfig.disconnectpad"] = new()
                {
                    [AppLanguage.English] = "Connection to the pad (XInput) was lost.",
                    [AppLanguage.Polish] = "Utracono połączenie z padem (XInput).",
                    [AppLanguage.Turkish] = "Kola (XInput) bağlantısı kesildi.",
                    [AppLanguage.German] = "Verbindung zum Controller (XInput) wurde unterbrochen.",
                    [AppLanguage.Spanish] = "Se perdió la conexión con el mando (XInput).",
                },
                ["wheelconfig.disconnectwheel"] = new()
                {
                    [AppLanguage.English] = "Connection to the wheel was lost.",
                    [AppLanguage.Polish] = "Utracono połączenie z kierownicą.",
                    [AppLanguage.Turkish] = "Direksiyon bağlantısı kesildi.",
                    [AppLanguage.German] = "Verbindung zum Lenkrad wurde unterbrochen.",
                    [AppLanguage.Spanish] = "Se perdió la conexión con el volante.",
                },
                ["wheelconfig.disconnecterror"] = new()
                {
                    [AppLanguage.English] = "Error connecting the wheel: ",
                    [AppLanguage.Polish] = "Błąd podłączania kierownicy: ",
                    [AppLanguage.Turkish] = "Direksiyon bağlanırken hata oluştu: ",
                    [AppLanguage.German] = "Fehler beim Verbinden des Lenkrads: ",
                    [AppLanguage.Spanish] = "Error al conectar el volante: ",
                },
                ["wheelconfig.noxinput"] = new()
                {
                    [AppLanguage.English] = "No active XInput pad found.",
                    [AppLanguage.Polish] = "Nie znaleziono aktywnego pada XInput.",
                    [AppLanguage.Turkish] = "Aktif bir XInput kolu bulunamadı.",
                    [AppLanguage.German] = "Kein aktiver XInput-Controller gefunden.",
                    [AppLanguage.Spanish] = "No se encontró ningún mando XInput activo.",
                },
                ["hud.lastlapscore"] = new()
                {
                    [AppLanguage.English] = "LAST LAP SCORE",
                    [AppLanguage.Polish] = "OSTATNIE OKRĄŻENIE",
                    [AppLanguage.Turkish] = "SON TUR PUANI",
                    [AppLanguage.German] = "LETZTE RUNDE",
                    [AppLanguage.Spanish] = "ÚLTIMA VUELTA",
                },

                ["hud.lapstart"] = new()
                {
                    [AppLanguage.English] = "START",
                    [AppLanguage.Polish] = "START",
                    [AppLanguage.Turkish] = "BAŞLANGIÇ",
                    [AppLanguage.German] = "START",
                    [AppLanguage.Spanish] = "INICIO",
                },
                ["rev.calibration_prompt.title"] = new()
                {
                    [AppLanguage.English] = "Rev Limiter Calibration",
                    [AppLanguage.Polish] = "Kalibracja ogranicznika obrotów",
                    [AppLanguage.Turkish] = "Devir Sınırlayıcı Kalibrasyonu",
                    [AppLanguage.German] = "Drehzahlbegrenzer-Kalibrierung",
                    [AppLanguage.Spanish] = "Calibración del limitador de RPM",
                },
                ["rev.calibration_prompt.body"] = new()
                {
                    [AppLanguage.English] =
                        "No rev limiter preset is saved yet for {0}. To calibrate the maximum RPM, " +
                        "click the button below or press your bound calibrate button/key.",
                    [AppLanguage.Polish] =
                        "Dla {0} nie zapisano jeszcze ustawień rev limitera. Aby dokonać kalibracji " +
                        "maksymalnych obrotów, kliknij przycisk poniżej lub użyj przypisanego przycisku/klawisza.",
                    [AppLanguage.Turkish] =
                        "{0} için henüz kaydedilmiş bir devir sınırlayıcı ayarı yok. Maksimum RPM'i kalibre " +
                        "etmek için aşağıdaki düğmeye tıklayın ya da atanmış düğmenizi/tuşunuzu kullanın.",
                    [AppLanguage.German] =
                        "Für {0} ist noch kein Drehzahlbegrenzer-Preset gespeichert. Um die maximale " +
                        "Drehzahl zu kalibrieren, klicke auf die Schaltfläche unten oder benutze deine " +
                        "zugewiesene Taste/deinen Knopf.",
                    [AppLanguage.Spanish] =
                        "Aún no hay un ajuste de limitador de RPM guardado para {0}. Para calibrar las RPM " +
                        "máximas, haz clic en el botón de abajo o usa tu botón/tecla asignada.",
                },

                ["rev.calibration_prompt.manual_body"] = new()
                {
                    [AppLanguage.English] =
                        "Recalibrating the maximum RPM for {0}. Click the button below or press " +
                        "your bound calibrate button/key.",
                    [AppLanguage.Polish] =
                        "Przekalibrowywanie maksymalnych obrotów dla {0}. Kliknij przycisk poniżej " +
                        "lub użyj przypisanego przycisku/klawisza.",
                    [AppLanguage.Turkish] =
                        "{0} için maksimum RPM yeniden kalibre ediliyor. Aşağıdaki düğmeye tıklayın " +
                        "ya da atanmış düğmenizi/tuşunuzu kullanın.",
                    [AppLanguage.German] =
                        "Die maximale Drehzahl für {0} wird neu kalibriert. Klicke auf die Schaltfläche " +
                        "unten oder benutze deine zugewiesene Taste/deinen Knopf.",
                    [AppLanguage.Spanish] =
                        "Recalibrando las RPM máximas para {0}. Haz clic en el botón de abajo o usa " +
                        "tu botón/tecla asignada.",
                },
                ["rev.calibration_prompt.enter_hint"] = new()
                {
                    [AppLanguage.English] = " Or just press Enter.",
                    [AppLanguage.Polish] = " Albo po prostu wciśnij Enter.",
                    [AppLanguage.Turkish] = " Ya da sadece Enter tuşuna basın.",
                    [AppLanguage.German] = " Oder drücke einfach Enter.",
                    [AppLanguage.Spanish] = " O simplemente presiona Enter.",
                },
                ["rev.calibration_prompt.inprogress"] = new()
                {
                    [AppLanguage.English] = "Shift to neutral and hold full throttle",
                    [AppLanguage.Polish] = "Wrzuć neutralny bieg i wciśnij gaz do dechy",
                    [AppLanguage.Turkish] = "Vitesi boşa alın ve gaza sonuna kadar basılı tutun",
                    [AppLanguage.German] = "Schalte in den Leerlauf und halte das Gaspedal durchgetreten",
                    [AppLanguage.Spanish] = "Pon punto muerto y mantén el acelerador a fondo",
                },

                ["rev.calibration_prompt.done"] = new()
                {
                    [AppLanguage.English] = "Successfully calibrated maximum engine RPM",
                    [AppLanguage.Polish] = "Pomyślnie skalibrowano maksymalne obroty silnika",
                    [AppLanguage.Turkish] = "Maksimum motor devri başarıyla kalibre edildi",
                    [AppLanguage.German] = "Maximale Motordrehzahl erfolgreich kalibriert",
                    [AppLanguage.Spanish] = "RPM máximas del motor calibradas correctamente",
                },

                ["status.missing_sounds"] = new()
                {
                    [AppLanguage.English] = "Missing sound files in the Sounds folder: {0}",
                    [AppLanguage.Polish] = "Brak plików dźwiękowych w folderze Sounds: {0}",
                    [AppLanguage.Turkish] = "Sounds klasöründe eksik ses dosyaları: {0}",
                    [AppLanguage.German] = "Fehlende Sounddateien im Ordner Sounds: {0}",
                    [AppLanguage.Spanish] = "Faltan archivos de sonido en la carpeta Sounds: {0}",
                },
                ["rev.loaded_for_car"] = new()
                {
                    [AppLanguage.English] = "Loaded rev limiter settings for {0}: {1} RPM / {2} ms",
                    [AppLanguage.Polish] = "Wczytano ustawienia rev limitera dla {0}: {1} RPM / {2} ms",
                    [AppLanguage.Turkish] = "{0} için devir sınırlayıcı ayarları yüklendi: {1} RPM / {2} ms",
                    [AppLanguage.German] = "Drehzahlbegrenzer-Einstellungen für {0} geladen: {1} U/min / {2} ms",
                    [AppLanguage.Spanish] = "Ajustes del limitador de RPM cargados para {0}: {1} RPM / {2} ms",
                },
                ["rev.no_saved_for_car"] = new()
                {
                    [AppLanguage.English] = "{0}: no saved rev limiter settings — using default {1} RPM / {2} ms",
                    [AppLanguage.Polish] = "{0}: brak zapisanych ustawień rev limitera — użyto domyślnych {1} RPM / {2} ms",
                    [AppLanguage.Turkish] = "{0}: kayıtlı devir sınırlayıcı ayarı yok — varsayılan {1} RPM / {2} ms kullanılıyor",
                    [AppLanguage.German] = "{0}: keine gespeicherten Drehzahlbegrenzer-Einstellungen — Standard {1} U/min / {2} ms wird verwendet",
                    [AppLanguage.Spanish] = "{0}: no hay ajustes de limitador de RPM guardados — usando el valor predeterminado {1} RPM / {2} ms",
                },
                ["hud.showoverlay"] = new()
                {
                    [AppLanguage.English] = "Forza-style overlay",
                    [AppLanguage.Polish] = "Nakładka w stylu Forza",
                    [AppLanguage.Turkish] = "Forza tarzı katman",
                    [AppLanguage.German] = "Forza-Stil-Overlay",
                    [AppLanguage.Spanish] = "Superposición estilo Forza",
                },
                ["hud.showoverlay.subtitle"] = new()
                {
                    [AppLanguage.English] = "Draws a transparent Forza-style overlay window over the game (drift score, RPM/speed gauge).",
                    [AppLanguage.Polish] = "Rysuje przezroczyste okno nakładki w stylu Forza nad grą (wynik driftu, obrotomierz/prędkościomierz).",
                    [AppLanguage.Turkish] = "Oyunun üzerine Forza tarzı şeffaf bir katman çizer (drift puanı, RPM/hız göstergesi).",
                    [AppLanguage.German] = "Zeichnet ein transparentes Forza-Stil-Overlay-Fenster über dem Spiel (Drift-Punktzahl, Drehzahl-/Geschwindigkeitsanzeige).",
                    [AppLanguage.Spanish] = "Dibuja una ventana de superposición transparente estilo Forza sobre el juego (puntuación de derrape, velocímetro/tacómetro).",
                },
                ["indicators.bindname.left"] = new()
                {
                    [AppLanguage.English] = "LEFT TURN SIGNAL",
                    [AppLanguage.Polish] = "LEWY KIERUNKOWSKAZ",
                    [AppLanguage.Turkish] = "SOL SİNYAL",
                    [AppLanguage.German] = "LINKER BLINKER",
                    [AppLanguage.Spanish] = "INTERMITENTE IZQUIERDO",
                },
                ["indicators.bindname.right"] = new()
                {
                    [AppLanguage.English] = "RIGHT TURN SIGNAL",
                    [AppLanguage.Polish] = "PRAWY KIERUNKOWSKAZ",
                    [AppLanguage.Turkish] = "SAĞ SİNYAL",
                    [AppLanguage.German] = "RECHTER BLINKER",
                    [AppLanguage.Spanish] = "INTERMITENTE DERECHO",
                },
                ["indicators.bindname.hazard"] = new()
                {
                    [AppLanguage.English] = "HAZARD SIGNALS",
                    [AppLanguage.Polish] = "ŚWIATŁA AWARYJNE",
                    [AppLanguage.Turkish] = "DÖRTLÜ FLAŞÖR",
                    [AppLanguage.German] = "WARNBLINKER",
                    [AppLanguage.Spanish] = "LUCES DE EMERGENCIA",
                },
                ["indicators.bindname.lights"] = new()
                {
                    [AppLanguage.English] = "LIGHTS TOGGLE",
                    [AppLanguage.Polish] = "PRZEŁĄCZNIK ŚWIATEŁ",
                    [AppLanguage.Turkish] = "FAR AÇMA/KAPAMA",
                    [AppLanguage.German] = "LICHTSCHALTER",
                    [AppLanguage.Spanish] = "INTERRUPTOR DE LUCES",
                },
                ["wheelconfig.configured"] = new()
                {
                    [AppLanguage.English] = "Wheel configured: {0} (axis: {1})",
                    [AppLanguage.Polish] = "Kierownica skonfigurowana: {0} (oś: {1})",
                    [AppLanguage.Turkish] = "Direksiyon yapılandırıldı: {0} (eksen: {1})",
                    [AppLanguage.German] = "Lenkrad konfiguriert: {0} (Achse: {1})",
                    [AppLanguage.Spanish] = "Volante configurado: {0} (eje: {1})",
                },
                ["rev.calibration_award.start"] = new()
                {
                    [AppLanguage.English] = "REV LIMITER CALIBRATION - SELECT NEUTRAL AND HOLD FULL THROTTLE!!!",
                    [AppLanguage.Polish] = "KALIBRACJA OGRANICZNIKA OBROTÓW - WŁĄCZ LUZ I WCIŚNIJ GAZ DO DECHY!!!",
                    [AppLanguage.Turkish] = "DEVİR SINIRLAYICI KALİBRASYONU - BOŞA ALIN VE GAZA SONUNA KADAR BASIN!!!",
                    [AppLanguage.German] = "DREHZAHLBEGRENZER-KALIBRIERUNG - LEERLAUF EINLEGEN UND VOLLGAS GEBEN!!!",
                    [AppLanguage.Spanish] = "CALIBRACIÓN DEL LIMITADOR DE RPM - PUNTO MUERTO Y ACELERADOR A FONDO!!!",
                },
                ["rev.calibration_award.done"] = new()
                {
                    [AppLanguage.English] = "REV LIMITER CALIBRATION DONE!!!",
                    [AppLanguage.Polish] = "KALIBRACJA OGRANICZNIKA OBROTÓW ZAKOŃCZONA!!!",
                    [AppLanguage.Turkish] = "DEVİR SINIRLAYICI KALİBRASYONU TAMAMLANDI!!!",
                    [AppLanguage.German] = "DREHZAHLBEGRENZER-KALIBRIERUNG ABGESCHLOSSEN!!!",
                    [AppLanguage.Spanish] = "¡CALIBRACIÓN DEL LIMITADOR DE RPM COMPLETADA!!!",
                },
                ["hud.colors.subtitle"] = new()
                {
                    [AppLanguage.English] = "Speedo+Tacho — RGB and transparency",
                    [AppLanguage.Polish] = "Prędkościomierz+obrotomierz — RGB i przezroczystość",
                    [AppLanguage.Turkish] = "Hız+Devir — RGB ve saydamlık",
                    [AppLanguage.German] = "Tacho+Drehzahl — RGB und Transparenz",
                    [AppLanguage.Spanish] = "Velocímetro+Tacómetro — RGB y transparencia",
                },
                ["hud.color.redline"] = new()
                {
                    [AppLanguage.English] = "Redline",
                    [AppLanguage.Polish] = "Czerwone pole",
                    [AppLanguage.Turkish] = "Kırmızı çizgi",
                    [AppLanguage.German] = "Rote Linie",
                    [AppLanguage.Spanish] = "Zona roja",
                },
                ["hud.color.text"] = new()
                {
                    [AppLanguage.English] = "Text",
                    [AppLanguage.Polish] = "Tekst",
                    [AppLanguage.Turkish] = "Metin",
                    [AppLanguage.German] = "Text",
                    [AppLanguage.Spanish] = "Texto",
                },
                ["hud.color.indicator"] = new()
                {
                    [AppLanguage.English] = "Indicator",
                    [AppLanguage.Polish] = "Wskaźnik",
                    [AppLanguage.Turkish] = "Gösterge",
                    [AppLanguage.German] = "Zeiger",
                    [AppLanguage.Spanish] = "Indicador",
                },
                ["hud.color.ticks"] = new()
                {
                    [AppLanguage.English] = "Ticks",
                    [AppLanguage.Polish] = "Kreski",
                    [AppLanguage.Turkish] = "Çizikler",
                    [AppLanguage.German] = "Skalenstriche",
                    [AppLanguage.Spanish] = "Marcas",
                },
                ["hud.color.background"] = new()
                {
                    [AppLanguage.English] = "Background",
                    [AppLanguage.Polish] = "Tło",
                    [AppLanguage.Turkish] = "Arka plan",
                    [AppLanguage.German] = "Hintergrund",
                    [AppLanguage.Spanish] = "Fondo",
                },
                ["insim.colorpicker.title"] = new()
                {
                    [AppLanguage.English] = "InSim Color Picker",
                    [AppLanguage.Polish] = "Wybór koloru InSim",
                    [AppLanguage.Turkish] = "InSim Renk Seçici",
                    [AppLanguage.German] = "InSim-Farbwähler",
                    [AppLanguage.Spanish] = "Selector de color InSim",
                },
                ["collision.hit"] = new()
                {
                    [AppLanguage.English] = "{0} HIT! -{1}",
                    [AppLanguage.Polish] = "{0} UDERZENIE! -{1}",
                    [AppLanguage.Turkish] = "{0} ÇARPMA! -{1}",
                    [AppLanguage.German] = "{0} TREFFER! -{1}",
                    [AppLanguage.Spanish] = "{0} ¡GOLPE! -{1}",
                },
                ["collision.kiss"] = new()
                {
                    [AppLanguage.English] = "{0} KISS! +{1}",
                    [AppLanguage.Polish] = "{0} MUŚNIĘCIE! +{1}",
                    [AppLanguage.Turkish] = "{0} SIYIRMA! +{1}",
                    [AppLanguage.German] = "{0} STREIFER! +{1}",
                    [AppLanguage.Spanish] = "{0} ¡ROCE! +{1}",
                },
                ["collision.car_kiss"] = new()
                {
                    [AppLanguage.English] = "{0} KISS! +{1}",
                    [AppLanguage.Polish] = "{0} MUŚNIĘCIE! +{1}",
                    [AppLanguage.Turkish] = "{0} SIYIRMA! +{1}",
                    [AppLanguage.German] = "{0} STREIFER! +{1}",
                    [AppLanguage.Spanish] = "{0} ¡ROCE! +{1}",
                },
                ["collision.car_penalty"] = new()
                {
                    [AppLanguage.English] = "{0} {1}! -{2}",
                    [AppLanguage.Polish] = "{0} {1}! -{2}",
                    [AppLanguage.Turkish] = "{0} {1}! -{2}",
                    [AppLanguage.German] = "{0} {1}! -{2}",
                    [AppLanguage.Spanish] = "{0} ¡{1}! -{2}",
                },
                ["indicators.sound_error"] = new()
                {
                    [AppLanguage.English] = "Indicator sound error: {0}",
                    [AppLanguage.Polish] = "Błąd dźwięku kierunkowskazu: {0}",
                    [AppLanguage.Turkish] = "Sinyal ses hatası: {0}",
                    [AppLanguage.German] = "Fehler beim Blinkerton: {0}",
                    [AppLanguage.Spanish] = "Error de sonido del intermitente: {0}",
                },
                ["hud.customcolor.title"] = new()
                {
                    [AppLanguage.English] = "Custom Overlay Color",
                    [AppLanguage.Polish] = "Własny kolor nakładki",
                    [AppLanguage.Turkish] = "Özel Katman Rengi",
                    [AppLanguage.German] = "Benutzerdefinierte Overlay-Farbe",
                    [AppLanguage.Spanish] = "Color personalizado de superposición",
                },
                ["rev.cut"] = new()
                {
                    [AppLanguage.English] = "⚡ CUT",
                    [AppLanguage.Polish] = "⚡ CIĘCIE",
                    [AppLanguage.Turkish] = "⚡ KESME",
                    [AppLanguage.German] = "⚡ ABSCHALTUNG",
                    [AppLanguage.Spanish] = "⚡ CORTE",
                },
                ["common.ok"] = new()
                {
                    [AppLanguage.English] = "OK",
                    [AppLanguage.Polish] = "OK",
                    [AppLanguage.Turkish] = "Tamam",
                    [AppLanguage.German] = "OK",
                    [AppLanguage.Spanish] = "Aceptar",
                },
            };
    }
}