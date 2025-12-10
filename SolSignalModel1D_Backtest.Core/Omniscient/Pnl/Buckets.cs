using SolSignalModel1D_Backtest.Core.Omniscient.Pnl;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	/// <summary>
	/// Частичный класс PnlCalculator: бакеты капитала и обновление их equity.
	/// Здесь сосредоточена логика выделения долей капитала и учёта просадки/выводов.
	/// </summary>
	public static partial class PnlCalculator
		{
		/// <summary>
		/// Внутреннее состояние одного бакета капитала.
		/// </summary>
		private sealed class BucketState
			{
			public string Name = string.Empty;
			public double BaseCapital;
			public double Equity;
			public double PeakVisible;
			public double MaxDd;
			public double Withdrawn;
			public bool IsDead;
			}

		/// <summary>
		/// Инициализация трёх бакетов: daily / intraday / delayed.
		/// Доли берутся из констант *Share и суммарного TotalCapital.
		/// </summary>
		private static Dictionary<string, BucketState> InitBuckets ()
			=> new (StringComparer.OrdinalIgnoreCase)
				{
				["daily"] = MakeBucket ("daily", TotalCapital * DailyShare),
				["intraday"] = MakeBucket ("intraday", TotalCapital * IntradayShare),
				["delayed"] = MakeBucket ("delayed", TotalCapital * DelayedShare),
				};

		/// <summary>
		/// Создаёт бакет с заданным базовым капиталом.
		/// </summary>
		private static BucketState MakeBucket ( string name, double baseCapital ) => new ()
			{
			Name = name,
			BaseCapital = baseCapital,
			Equity = baseCapital,
			PeakVisible = baseCapital,
			MaxDd = 0.0,
			Withdrawn = 0.0,
			IsDead = false
			};

		/// <summary>
		/// Обновление equity бакета с учётом PnL, комиссий, ликвидаций и выводов.
		/// Для Cross: ликвидация или уход equity в ноль означают смерть бакета.
		/// Для Isolated: при ликвидации теряется только marginUsed + комиссия;
		/// бакет после этого считается мёртвым, даже если формально equity > 0.
		/// </summary>
		private static void UpdateBucketEquity (
			MarginMode marginMode,
			BucketState bucket,
			double marginUsed,
			double positionPnl,
			double positionComm,
			bool priceLiquidated,
			ref double withdrawnLocal,
			out bool died )
			{
			if (bucket == null)
				throw new ArgumentNullException (nameof (bucket));

			if (marginUsed < 0.0)
				throw new InvalidOperationException ("[pnl] marginUsed must be non-negative in UpdateBucketEquity().");

			died = false;
			double newEquity = bucket.Equity;

			if (marginMode == MarginMode.Cross)
				{
				if (priceLiquidated)
					{
					// При ликвидации в Cross считаем, что весь бакет обнуляется.
					newEquity = 0.0;
					bucket.IsDead = true;
					died = true;
					}
				else
					{
					newEquity = bucket.Equity + positionPnl - positionComm;

					// Если equity ушла в ноль или ниже без явной ценовой ликвидации,
					// это тоже трактуется как смерть бакета.
					if (newEquity <= 0.0)
						{
						newEquity = 0.0;
						bucket.IsDead = true;
						died = true;
						}
					}

				// Выводим прибыль сверх базового капитала только если бакет жив.
				if (!died && newEquity > bucket.BaseCapital)
					{
					double extra = newEquity - bucket.BaseCapital;
					if (extra > 0)
						{
						bucket.Withdrawn += extra;
						withdrawnLocal += extra;
						}
					newEquity = bucket.BaseCapital;
					}
				}
			else // Isolated
				{
				if (priceLiquidated)
					{
					// При изолированной марже при ликвидации теряется marginUsed + комиссия.
					newEquity = bucket.Equity - marginUsed - positionComm;
					if (newEquity < 0) newEquity = 0.0;
					bucket.IsDead = true;
					died = true;
					}
				else
					{
					newEquity = bucket.Equity + positionPnl - positionComm;

					if (newEquity <= 0.0)
						{
						// Полная потеря equity без прямого срабатывания ликвиды.
						newEquity = 0.0;
						bucket.IsDead = true;
						died = true;
						}
					else if (newEquity > bucket.BaseCapital)
						{
						// Фиксируем прибыль сверх базового капитала и выводим её.
						double extra = newEquity - bucket.BaseCapital;
						bucket.Withdrawn += extra;
						withdrawnLocal += extra;
						newEquity = bucket.BaseCapital;
						}
					}
				}

			bucket.Equity = newEquity;

			// Пик «видимой» equity = equity + withdrawals.
			double visible = bucket.Equity + bucket.Withdrawn;
			if (visible > bucket.PeakVisible)
				bucket.PeakVisible = visible;

			// Макс. просадка считается по «видимой» equity.
			if (bucket.PeakVisible > 1e-9)
				{
				double dd = (bucket.PeakVisible - visible) / bucket.PeakVisible;
				if (dd > bucket.MaxDd) bucket.MaxDd = dd;
				}
			}
		}
	}
