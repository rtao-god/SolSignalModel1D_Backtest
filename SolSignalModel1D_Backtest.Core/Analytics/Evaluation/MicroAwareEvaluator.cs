using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Micro;
using System;

namespace SolSignalModel1D_Backtest.Core.Analytics.Evaluation
	{
	/// <summary>
	/// Метрики/оценка предсказаний, которым нужны forward-факты (label, micro ground-truth).
	/// Это не должно находиться в causal PredictionEngine, иначе легко получить утечку через «невинный» доступ к фактам.
	/// </summary>
	public static class MicroAwareEvaluator
		{
		public static bool EvalMicroAware ( OmniscientDataRow r, int predClass, MicroInfo micro )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));

			int fact = r.Outcomes.Label;
			bool factMicroUp = r.Outcomes.FactMicroUp;
			bool factMicroDown = r.Outcomes.FactMicroDown;

			bool baseCorrect = predClass == fact;
			if (baseCorrect) return true;

			if (fact == 2 && predClass == 1 && micro.ConsiderUp) return true;
			if (fact == 0 && predClass == 1 && micro.ConsiderDown) return true;

			if (fact == 1 && factMicroUp && predClass == 2) return true;
			if (fact == 1 && factMicroDown && predClass == 0) return true;

			if (fact == 1 && factMicroUp && predClass == 1 && micro.ConsiderUp) return true;
			if (fact == 1 && factMicroDown && predClass == 1 && micro.ConsiderDown) return true;

			return false;
			}

		public static double EvalWeighted ( OmniscientDataRow r, int predClass, MicroInfo micro )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));

			int fact = r.Outcomes.Label;

			bool predMicroUp = micro.ConsiderUp;
			bool predMicroDown = micro.ConsiderDown;

			bool factMicroUp = r.Outcomes.FactMicroUp;
			bool factMicroDown = r.Outcomes.FactMicroDown;

			if (fact == 2)
				{
				if (predClass == 2) return 1.0;
				if (predClass == 1 && predMicroUp) return 1.0;
				if (predClass == 1) return 0.25;
				return 0.0;
				}

			if (fact == 0)
				{
				if (predClass == 0) return 1.0;
				if (predClass == 1 && predMicroDown) return 1.0;
				if (predClass == 1) return 0.25;
				return 0.0;
				}

			if (fact == 1 && factMicroUp)
				{
				if (predClass == 1 && predMicroUp) return 1.0;
				if (predClass == 2) return 0.8;
				if (predClass == 1) return 0.2;
				return 0.0;
				}

			if (fact == 1 && factMicroDown)
				{
				if (predClass == 1 && predMicroDown) return 1.0;
				if (predClass == 0) return 0.8;
				if (predClass == 1) return 0.2;
				return 0.0;
				}

			if (fact == 1)
				{
				if (predClass == 1) return 1.0;
				return 0.3;
				}

			return 0.0;
			}
		}
	}
