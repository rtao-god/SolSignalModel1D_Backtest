using SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Contracts;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Adapters
	{
	/// <summary>
	/// Адаптер слоя домена (BacktestRecord) к минимальному контракту аналитики.
	/// Здесь допустима зависимость от BacktestRecord; дальше по цепочке — нет.
	/// </summary>
	public static class BacktestRecordProjection
		{
		public static BacktestAggRow ToAggRow ( this BacktestRecord r )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));

			// Инварианты микро-флагов лучше ловить тут, чтобы не размазывать по билдеру/принтеру.
			if (r.PredMicroUp && r.PredMicroDown)
				{
				throw new InvalidOperationException (
					$"[proj] Invalid micro prediction flags: both PredMicroUp and PredMicroDown are true for {r.DateUtc:O}.");
				}

			if (r.FactMicroUp && r.FactMicroDown)
				{
				throw new InvalidOperationException (
					$"[proj] Invalid micro fact flags: both FactMicroUp and FactMicroDown are true for {r.DateUtc:O}.");
				}

			return new BacktestAggRow
				{
				DateUtc = r.DateUtc,
				TrueLabel = r.TrueLabel,

				PredLabel_Day = r.PredLabel_Day,
				PredLabel_DayMicro = r.PredLabel_DayMicro,
				PredLabel_Total = r.PredLabel_Total,

				ProbUp_Day = r.ProbUp_Day,
				ProbFlat_Day = r.ProbFlat_Day,
				ProbDown_Day = r.ProbDown_Day,

				ProbUp_DayMicro = r.ProbUp_DayMicro,
				ProbFlat_DayMicro = r.ProbFlat_DayMicro,
				ProbDown_DayMicro = r.ProbDown_DayMicro,

				ProbUp_Total = r.ProbUp_Total,
				ProbFlat_Total = r.ProbFlat_Total,
				ProbDown_Total = r.ProbDown_Total,

				Conf_Day = r.Conf_Day,
				Conf_Micro = r.Conf_Micro,

				SlProb = r.SlProb,
				SlHighDecision = r.SlHighDecision,

				PredMicroUp = r.PredMicroUp,
				PredMicroDown = r.PredMicroDown,
				FactMicroUp = r.FactMicroUp,
				FactMicroDown = r.FactMicroDown
				};
			}
		}
	}
