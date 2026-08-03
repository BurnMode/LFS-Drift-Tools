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

            // fallback: angielski, potem sam klucz
            if (_strings.TryGetValue(key, out var t2) && t2.TryGetValue(AppLanguage.English, out var en))
                return en;

            return key;
        }

        private static readonly Dictionary<string, Dictionary<AppLanguage, string>> _strings =
            new Dictionary<string, Dictionary<AppLanguage, string>>
            {
                ["header.title"] = new()
                {
                    [AppLanguage.English] = "Live For Speed - Drift Tools",
                    [AppLanguage.Polish] = "Live For Speed - Drift Tools",
                    [AppLanguage.Turkish] = "Live For Speed - Drift Tools",
                    [AppLanguage.German] = "Live For Speed - Drift Tools",
                    [AppLanguage.Spanish] = "Live For Speed - Drift Tools",
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
                    [AppLanguage.English] = "THEME",
                    [AppLanguage.Polish] = "MOTYW",
                    [AppLanguage.Turkish] = "TEMA",
                    [AppLanguage.German] = "DESIGN",
                    [AppLanguage.Spanish] = "TEMA",
                },
                ["header.language"] = new()
                {
                    [AppLanguage.English] = "LANGUAGE",
                    [AppLanguage.Polish] = "JĘZYK",
                    [AppLanguage.Turkish] = "DİL",
                    [AppLanguage.German] = "SPRACHE",
                    [AppLanguage.Spanish] = "IDIOMA",
                },
                ["status.disconnected"] = new()
                {
                    [AppLanguage.English] = "⬤  Disconnected",
                    [AppLanguage.Polish] = "⬤  Rozłączono",
                    [AppLanguage.Turkish] = "⬤  Bağlantı Kesildi",
                    [AppLanguage.German] = "⬤  Getrennt",
                    [AppLanguage.Spanish] = "⬤  Desconectado",
                },
                ["status.connected"] = new()
                {
                    [AppLanguage.English] = "⬤  Connected with LFS",
                    [AppLanguage.Polish] = "⬤  Połączono z LFS",
                    [AppLanguage.Turkish] = "⬤  LFS ile Bağlandı",
                    [AppLanguage.German] = "⬤  Mit LFS verbunden",
                    [AppLanguage.Spanish] = "⬤  Conectado con LFS",
                },
                ["status.hint"] = new()
                {
                    [AppLanguage.English] = "Type /insim 29999 in LFS, and click CONNECT.",
                    [AppLanguage.Polish] = "Wpisz /insim 29999 w LFS, i kliknij CONNECT.",
                    [AppLanguage.Turkish] = "LFS içinde /insim 29999 yazın ve CONNECT'e tıklayın.",
                    [AppLanguage.German] = "Gib /insim 29999 in LFS ein und klicke auf CONNECT.",
                    [AppLanguage.Spanish] = "Escribe /insim 29999 en LFS y haz clic en CONNECT.",
                },
                ["connection.title"] = new()
                {
                    [AppLanguage.English] = "Connection",
                    [AppLanguage.Polish] = "Połączenie",
                    [AppLanguage.Turkish] = "Bağlantı",
                    [AppLanguage.German] = "Verbindung",
                    [AppLanguage.Spanish] = "Conexión",
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
                    [AppLanguage.English] = "Host:",
                    [AppLanguage.Polish] = "Host:",
                    [AppLanguage.Turkish] = "Host:",
                    [AppLanguage.German] = "Host:",
                    [AppLanguage.Spanish] = "Host:",
                },
                ["connection.port"] = new()
                {
                    [AppLanguage.English] = "Port:",
                    [AppLanguage.Polish] = "Port:",
                    [AppLanguage.Turkish] = "Port:",
                    [AppLanguage.German] = "Port:",
                    [AppLanguage.Spanish] = "Puerto:",
                },
                ["connection.adminpass"] = new()
                {
                    [AppLanguage.English] = "Admin password:",
                    [AppLanguage.Polish] = "Hasło admina:",
                    [AppLanguage.Turkish] = "Yönetici şifresi:",
                    [AppLanguage.German] = "Admin-Passwort:",
                    [AppLanguage.Spanish] = "Contraseña de administrador:",
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
                ["hud.title"] = new()
                {
                    [AppLanguage.English] = "HUD Settings",
                    [AppLanguage.Polish] = "Ustawienia HUD",
                    [AppLanguage.Turkish] = "HUD Ayarları",
                    [AppLanguage.German] = "HUD-Einstellungen",
                    [AppLanguage.Spanish] = "Configuración del HUD",
                },
                ["hud.show"] = new()
                {
                    [AppLanguage.English] = "Show ingame HUD (IS_BTN)",
                    [AppLanguage.Polish] = "Pokaż HUD w grze (IS_BTN)",
                    [AppLanguage.Turkish] = "Oyun içi HUD'u göster (IS_BTN)",
                    [AppLanguage.German] = "Ingame-HUD anzeigen (IS_BTN)",
                    [AppLanguage.Spanish] = "Mostrar HUD en el juego (IS_BTN)",
                },
                ["hud.REVLimitter"] = new()
                {
                    [AppLanguage.English] = "Show ingame REV Limitter HUD",
                    [AppLanguage.Polish] = "Pokaż REV Limitter HUD w grze",
                    [AppLanguage.Turkish] = "Oyun içi REV Limiter HUD'unu",
                    [AppLanguage.German] = "Ingame-Drehzahlbegrenzer-HUD anzeigen",
                    [AppLanguage.Spanish] = "Mostrar HUD del limitador de RPM en el juego",
                },
                ["rev.title"] = new()
                {
                    [AppLanguage.English] = "Rev Limiter",
                    [AppLanguage.Polish] = "Ogranicznik obrotów",
                    [AppLanguage.Turkish] = "Devir Sınırlayıcı",
                    [AppLanguage.German] = "Drehzahlbegrenzer",
                    [AppLanguage.Spanish] = "Limitador de RPM",
                },
                ["rev.calibratehint"] = new()
                {
                    [AppLanguage.English] = "Bind functions using the gear icon.",
                    [AppLanguage.Polish] = "Przypisz funkcje klawiszem trybika.",
                    [AppLanguage.Turkish] = "Fonksiyonları dişli simgesiyle atayın.",
                    [AppLanguage.German] = "Funktionen über das Zahnradsymbol zuweisen.",
                    [AppLanguage.Spanish] = "Asigna funciones con el icono de engranaje.",
                },
                ["rev.calibrate"] = new()
                {
                    [AppLanguage.English] = "CALIBRATE",
                    [AppLanguage.Polish] = "KALIBRUJ",
                    [AppLanguage.Turkish] = "KALİBRE ET",
                    [AppLanguage.German] = "KALIBRIEREN",
                    [AppLanguage.Spanish] = "CALIBRAR",
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
                    [AppLanguage.English] = "RPM LIMIT: ",
                    [AppLanguage.Polish] = "LIMIT RPM: ",
                    [AppLanguage.Turkish] = "RPM LİMİTİ: ",
                    [AppLanguage.German] = "DREHZAHLGRENZE: ",
                    [AppLanguage.Spanish] = "LÍMITE DE RPM: ",
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
                    [AppLanguage.Polish] = "Bindowanie ogranicznika obrotów",
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
                ["indicators.lights"] = new()
                {
                    [AppLanguage.English] = "LIGHTS",
                    [AppLanguage.Polish] = "ŚWIATŁA",
                    [AppLanguage.Turkish] = "FARLAR",
                    [AppLanguage.German] = "LICHTER",
                    [AppLanguage.Spanish] = "LUCES",
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
                ["keybind.dialog.title"] = new()
                {
                    [AppLanguage.English] = "Key binding",
                    [AppLanguage.Polish] = "Bindowanie klawisza",
                    [AppLanguage.Turkish] = "Tuş atama",
                    [AppLanguage.German] = "Tastenzuweisung",
                    [AppLanguage.Spanish] = "Asignación de tecla",
                },
                ["keybind.dialog.press"] = new()
                {
                    [AppLanguage.English] = "Press a key for: {0}\n\n(Press ESC to cancel)",
                    [AppLanguage.Polish] = "Naciśnij klawisz dla: {0}\n\n(Naciśnij ESC aby anulować)",
                    [AppLanguage.Turkish] = "{0} için bir tuşa basın\n\n(İptal için ESC)",
                    [AppLanguage.German] = "Drücke eine Taste für: {0}\n\n(ESC zum Abbrechen)",
                    [AppLanguage.Spanish] = "Presiona una tecla para: {0}\n\n(Presiona ESC para cancelar)",
                },
                ["keybind.dialog.bound"] = new()
                {
                    [AppLanguage.English] = "Bound: {0}\n\nClick to return...",
                    [AppLanguage.Polish] = "Bindowano: {0}\n\nKlikaj by wrócić...",
                    [AppLanguage.Turkish] = "Atandı: {0}\n\nDönmek için tıklayın...",
                    [AppLanguage.German] = "Zugewiesen: {0}\n\nKlicken zum Zurückkehren...",
                    [AppLanguage.Spanish] = "Asignado: {0}\n\nHaz clic para volver...",
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
            };
    }
}