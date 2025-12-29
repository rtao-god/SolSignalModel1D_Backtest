namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Pnl
	{
	public static partial class PnlCalculator
		{
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

		private static Dictionary<string, BucketState> InitBuckets ()
			=> new (StringComparer.OrdinalIgnoreCase)
				{
				["daily"] = MakeBucket ("daily", TotalCapital * DailyShare),
				["intraday"] = MakeBucket ("intraday", TotalCapital * IntradayShare),
				["delayed"] = MakeBucket ("delayed", TotalCapital * DelayedShare),
				};

		private static BucketState MakeBucket ( string name, double baseCapital )
			{
			if (string.IsNullOrWhiteSpace (name))
				throw new ArgumentException ("bucket name must not be empty", nameof (name));

			if (baseCapital < 0.0)
				throw new ArgumentOutOfRangeException (nameof (baseCapital), "baseCapital must be non-negative");

			return new BucketState
				{
				Name = name,
				BaseCapital = baseCapital,
				Equity = baseCapital,
				PeakVisible = baseCapital,
				MaxDd = 0.0,
				Withdrawn = 0.0,
				IsDead = false
				};
			}

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
			if (bucket == null) throw new ArgumentNullException (nameof (bucket));
			if (marginUsed < 0.0) throw new InvalidOperationException ("[pnl] marginUsed must be non-negative.");

			died = false;

			double newEquity = bucket.Equity;

			if (marginMode == MarginMode.Cross)
				{
				if (priceLiquidated)
					{
					newEquity = 0.0;
					bucket.IsDead = true;
					died = true;
					}
				else
					{
					newEquity = bucket.Equity + positionPnl - positionComm;

					if (newEquity <= 0.0)
						{
						newEquity = 0.0;
						bucket.IsDead = true;
						died = true;
						}
					}

				// Вывод прибыли сверх базового капитала делаем только если бакет жив.
				if (!died && newEquity > bucket.BaseCapital)
					{
					double extra = newEquity - bucket.BaseCapital;
					if (extra > 0.0)
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
					// При изолированной марже считаем, что теряется marginUsed + комиссия.
					newEquity = bucket.Equity - marginUsed - positionComm;
					if (newEquity < 0.0) newEquity = 0.0;

					bucket.IsDead = true;
					died = true;
					}
				else
					{
					newEquity = bucket.Equity + positionPnl - positionComm;

					if (newEquity <= 0.0)
						{
						newEquity = 0.0;
						bucket.IsDead = true;
						died = true;
						}
					else if (newEquity > bucket.BaseCapital)
						{
						double extra = newEquity - bucket.BaseCapital;
						bucket.Withdrawn += extra;
						withdrawnLocal += extra;
						newEquity = bucket.BaseCapital;
						}
					}
				}

			bucket.Equity = newEquity;

			// Peak/DD считаем по "видимой" equity = equity + withdrawals.
			double visible = bucket.Equity + bucket.Withdrawn;
			if (visible > bucket.PeakVisible)
				bucket.PeakVisible = visible;

			if (bucket.PeakVisible > 1e-9)
				{
				double dd = (bucket.PeakVisible - visible) / bucket.PeakVisible;
				if (dd > bucket.MaxDd) bucket.MaxDd = dd;
				}
			}
		}
	}
