using System;
using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Evaluation
	{
	/// <summary>
	/// Омнисциентная оценка качества предикта с учётом micro-слоя.
	/// </summary>
	public static class MicroAwareEvaluator
		{
		public readonly struct Truth
			{
			public Truth ( int trueLabel, bool factMicroUp, bool factMicroDown )
				{
				TrueLabel = trueLabel;
				FactMicroUp = factMicroUp;
				FactMicroDown = factMicroDown;

				if (trueLabel < 0 || trueLabel > 2)
					throw new ArgumentOutOfRangeException (nameof (trueLabel), trueLabel, "TrueLabel must be in [0..2].");

				if (factMicroUp && factMicroDown)
					throw new InvalidOperationException ("[MicroAwareEvaluator] Invalid truth: FactMicroUp && FactMicroDown.");
				}

			public int TrueLabel { get; }
			public bool FactMicroUp { get; }
			public bool FactMicroDown { get; }
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

			if (fact == 1 && truth.FactMicroUp && cls == 2) return true;
			if (fact == 1 && truth.FactMicroDown && cls == 0) return true;

			if (fact == 1 && truth.FactMicroUp && cls == 1 && microUp) return true;
			if (fact == 1 && truth.FactMicroDown && cls == 1 && microDown) return true;

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

			if (fact == 1 && truth.FactMicroUp)
				{
				if (cls == 1 && predMicroUp) return 1.0;
				if (cls == 2) return 0.8;
				if (cls == 1) return 0.2;
				return 0.0;
				}

			if (fact == 1 && truth.FactMicroDown)
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
