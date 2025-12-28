namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.MinMove
	{
	/// <summary>
	/// Результат расчёта minMove на момент asOfUtc.
	/// Контракт:
	/// - MinMove/LocalVol/EwmaVol/QuantileUsed всегда заданы.
	/// - Остальные метрики nullable: null означает "в этой версии пайплайна не вычислялось".
	/// </summary>
	public sealed class MinMoveResult
		{
		public DateTime AsOfUtc { get; init; }

		public bool RegimeDown { get; init; }

		public double MinMove { get; set; }
		public double LocalVol { get; set; }
		public double EwmaVol { get; set; }
		public double QuantileUsed { get; set; }

		// Под асимметрию — по-прежнему nullable.
		public double? MinMoveUp { get; init; }
		public double? MinMoveDown { get; init; }

		// Раньше эти поля молча становились 0.0, что неотличимо от "валидного нуля".
		// Теперь null = "не вычислялось".
		public double? FlatShare30d { get; init; }
		public double? FlatShare90d { get; init; }
		public double? EconFloorUsed { get; init; }
		public double? EwmaVolUsed { get; init; }

		public string Notes { get; init; } = string.Empty;

		// ===== Явные OrThrow аксессоры =====

		public double GetFlatShare30dOrThrow ()
			{
			if (FlatShare30d is null)
				throw new InvalidOperationException ($"[min-move] FlatShare30d is not computed for {AsOfUtc:O}.");
			return FlatShare30d.Value;
			}

		public double GetFlatShare90dOrThrow ()
			{
			if (FlatShare90d is null)
				throw new InvalidOperationException ($"[min-move] FlatShare90d is not computed for {AsOfUtc:O}.");
			return FlatShare90d.Value;
			}

		public double GetEconFloorUsedOrThrow ()
			{
			if (EconFloorUsed is null)
				throw new InvalidOperationException ($"[min-move] EconFloorUsed is not computed for {AsOfUtc:O}.");
			return EconFloorUsed.Value;
			}

		public double GetEwmaVolUsedOrThrow ()
			{
			if (EwmaVolUsed is null)
				throw new InvalidOperationException ($"[min-move] EwmaVolUsed is not computed for {AsOfUtc:O}.");
			return EwmaVolUsed.Value;
			}
		}
	}
