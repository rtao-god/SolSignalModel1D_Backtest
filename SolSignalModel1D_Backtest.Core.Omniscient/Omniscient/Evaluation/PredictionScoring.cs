using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Evaluation
	{
	/// <summary>
	/// Omniscient-оценка качества: использует forward-факты и потому НЕ может быть частью каузального PredictionEngine.
	/// Инвариант: эти методы запрещено вызывать в слоях, где нельзя видеть Forward/TrueLabel.
	/// </summary>
	public static class PredictionScoring
		{
		public static bool EvalMicroAware ( BacktestRecord r, int predClass, MicroInfo micro )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));

			bool baseCorrect = predClass == r.Forward.TrueLabel;
			if (baseCorrect) return true;

			if (r.Forward.TrueLabel == 2 && predClass == 1 && micro.ConsiderUp) return true;
			if (r.Forward.TrueLabel == 0 && predClass == 1 && micro.ConsiderDown) return true;

			bool hasMicroTruth = r.MicroTruth.HasValue;
			bool factMicroUp = hasMicroTruth && r.MicroTruth.Value == MicroTruthDirection.Up;
			bool factMicroDown = hasMicroTruth && r.MicroTruth.Value == MicroTruthDirection.Down;

			if (r.Forward.TrueLabel == 1 && factMicroUp && predClass == 2) return true;
			if (r.Forward.TrueLabel == 1 && factMicroDown && predClass == 0) return true;

			if (r.Forward.TrueLabel == 1 && factMicroUp && predClass == 1 && micro.ConsiderUp) return true;
			if (r.Forward.TrueLabel == 1 && factMicroDown && predClass == 1 && micro.ConsiderDown) return true;

			return false;
			}

		public static double EvalWeighted ( BacktestRecord r, int predClass, MicroInfo micro )
			{
			if (r == null) throw new ArgumentNullException (nameof (r));

			int fact = r.Forward.TrueLabel;

			bool predMicroUp = micro.ConsiderUp;
			bool predMicroDown = micro.ConsiderDown;

			bool factMicroUp = r.MicroTruth.HasValue && r.MicroTruth.Value == MicroTruthDirection.Up;
			bool factMicroDown = r.MicroTruth.HasValue && r.MicroTruth.Value == MicroTruthDirection.Down;

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
