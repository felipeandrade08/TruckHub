using System;
using System.Collections.Generic;

namespace TruckHub;

public partial class MainWindow
{
    private int _brakeHeatLevel;                       // 0 = frio, 1 = quente, 2 = crítico
    private int _restAlertLevel;                       // 0 = nenhum, 1 = 30 min, 2 = 10 min
    private const float BrakeHotC = 200f, BrakeCriticalC = 300f;
    private readonly Dictionary<string, int> _wearAlertLevel = new();

    /// <summary>
    /// Alertas operacionais da HUD baseados só em telemetria real:
    /// pausa obrigatória (30 e 10 min antes) e desgaste de componentes (25%, 50%, 75%).
    /// </summary>
    private void CheckOperationalAlerts(TelemetrySnapshot data, Action<string, string, bool> alert)
    {
        CheckTachographAlerts(alert);
        try
        {
            // Descanso: RestStopMinutes = minutos até a próxima pausa necessária.
            var rest = data.RestStopMinutes;
            if (rest <= 0) _restAlertLevel = 0;
            else if (rest <= 10 && _restAlertLevel < 2)
            {
                _restAlertLevel = 2;
                alert("rest-10", $"DESCANSO • PARE EM {rest} MIN • PAUSA OBRIGATÓRIA", true);
            }
            else if (rest <= 30 && _restAlertLevel < 1)
            {
                _restAlertLevel = 1;
                alert("rest-30", $"DESCANSO • PAUSA RECOMENDADA EM {rest} MIN", false);
            }
            else if (rest > 40) _restAlertLevel = 0;

            // Freios: temperatura real da telemetria, com histerese para não repetir o aviso.
            var bt = data.BrakeTemperature;
            var heat = bt >= BrakeCriticalC ? 2 : bt >= BrakeHotC ? 1 : 0;
            if (heat < _brakeHeatLevel && bt < (_brakeHeatLevel == 2 ? BrakeCriticalC - 40f : BrakeHotC - 40f)) _brakeHeatLevel = heat;
            else if (heat > _brakeHeatLevel)
            {
                _brakeHeatLevel = heat;
                alert("brake-heat" + heat, heat == 2
                    ? $"FREIOS CRÍTICOS • {bt:0}°C • USE FREIO-MOTOR/RETARDER E REDUZA"
                    : $"FREIOS QUENTES • {bt:0}°C • ALIVIE COM FREIO-MOTOR", heat == 2);
            }

            // Desgaste: avisa uma vez por faixa; zera ao consertar.
            var wears = new (string Key, string Name, float Value)[]
            {
                ("engine", "MOTOR", data.WearEngine), ("transmission", "TRANSMISSÃO", data.WearTransmission),
                ("chassis", "CHASSI", data.WearChassis), ("wheels", "PNEUS/RODAS", data.WearWheels), ("cabin", "CABINE", data.WearCabin)
            };
            foreach (var w in wears)
            {
                WearHistory.Record(w.Key, data.OdometerKm, w.Value);
                var level = w.Value >= 0.75f ? 3 : w.Value >= 0.50f ? 2 : w.Value >= 0.25f ? 1 : 0;
                _wearAlertLevel.TryGetValue(w.Key, out var last);
                if (level < last) _wearAlertLevel[w.Key] = level;
                else if (level > last)
                {
                    _wearAlertLevel[w.Key] = level;
                    alert("wear-" + w.Key + level, $"MANUTENÇÃO • {w.Name} COM {w.Value * 100:0}% DE DESGASTE", level >= 3);
                }
            }
        }
        catch (Exception ex) { App.WriteUiCrashLog("OperationalAlerts", ex); }
    }
}
