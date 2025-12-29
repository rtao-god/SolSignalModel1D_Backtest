using SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.Evaluation
	{
	/// <summary>
	/// Омнисциентная оценка качества предикта с учётом micro-слоя.
	/// </summary>
	public static class MicroAwareEvaluator
		{
		public readonly struct Truth
			{
			public Truth ( int trueLabel, OptionalValue<MicroTruthDirection> microTruth )
				{
				TrueLabel = trueLabel;
				MicroTruth = microTruth;

				if (trueLabel < 0 || trueLabel > 2)
					throw new ArgumentOutOfRangeException (nameof (trueLabel), trueLabel, "TrueLabel must be in [0..2].");

				if (microTruth.HasValue && trueLabel != 1)
					throw new InvalidOperationException ("[MicroAwareEvaluator] MicroTruth must be missing for non-flat label.");
				}

			public int TrueLabel { get; }
			public OptionalValue<MicroTruthDirection> MicroTruth { get; }
			}

		public static bool IsCorrectMicroAware ( CausalPredictionRecord pred, Truth truth )
			{
			if (pred == null) throw new ArgumentNullException (nameof (pred));

			int fact = truth.TrueLabel;
			int cls = pred.PredLabel;

			bool microUp = pred.PredMicroUp;
			bool microDown = pred.PredMicroDown;

			// базовая точность по классу
			if (cls == fact) return true;

			// micro-правила (перенос старого EvalMicroAware, но теперь это omniscient)
			if (fact == 2 && cls == 1 && microUp) return true;
			if (fact == 0 && cls == 1 && microDown) return true;

			bool hasMicroTruth = truth.MicroTruth.HasValue;
			bool factMicroUp = hasMicroTruth && truth.MicroTruth.Value == MicroTruthDirection.Up;
			bool factMicroDown = hasMicroTruth && truth.MicroTruth.Value == MicroTruthDirection.Down;

			if (fact == 1 && factMicroUp && cls == 2) return true;
			if (fact == 1 && factMicroDown && cls == 0) return true;

			if (fact == 1 && factMicroUp && cls == 1 && microUp) return true;
			if (fact == 1 && factMicroDown && cls == 1 && microDown) return true;

			return false;
			}

		public static double ScoreWeighted ( CausalPredictionRecord pred, Truth truth )
			{
			if (pred == null) throw new ArgumentNullException (nameof (pred));

			int fact = truth.TrueLabel;
			int cls = pred.PredLabel;

			bool predMicroUp = pred.PredMicroUp;
			bool predMicroDown = pred.PredMicroDown;

			if (fact == 2)
				{
				if (cls == 2) return 1.0;
				if (cls == 1 && predMicroUp) return 1.0;
				if (cls == 1) return 0.25;
				return 0.0;
				}

			if (fact == 0)
				{
				if (cls == 0) return 1.0;
				if (cls == 1 && predMicroDown) return 1.0;
				if (cls == 1) return 0.25;
				return 0.0;
				}

			if (fact == 1 && truth.MicroTruth.HasValue && truth.MicroTruth.Value == MicroTruthDirection.Up)
				{
				if (cls == 1 && predMicroUp) return 1.0;
				if (cls == 2) return 0.8;
				if (cls == 1) return 0.2;
				return 0.0;
				}

			if (fact == 1 && truth.MicroTruth.HasValue && truth.MicroTruth.Value == MicroTruthDirection.Down)
				{
				if (cls == 1 && predMicroDown) return 1.0;
				if (cls == 0) return 0.8;
				if (cls == 1) return 0.2;
				return 0.0;
				}

			// fact == flat, без micro-fact
			if (fact == 1)
				{
				if (cls == 1) return 1.0;
				return 0.3;
				}

			return 0.0;
			}
		}
	}
