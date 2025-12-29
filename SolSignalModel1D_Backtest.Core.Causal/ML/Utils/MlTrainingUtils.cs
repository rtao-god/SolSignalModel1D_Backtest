using SolSignalModel1D_Backtest.Core.Causal.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Utils
	{
	/// <summary>
	/// Общие хелперы для подготовки данных к ML.NET:
	/// - строгая конвертация feature-vector -> float[] фиксированной длины (MlSchema.FeatureCount);
	/// - oversample для бинарных задач.
	/// </summary>
	public static class MlTrainingUtils
		{
		public static float[] ToFloatFixed ( ReadOnlyMemory<double> featuresVector )
			{
			var src = featuresVector.Span;
			int expected = MlSchema.FeatureCount;

			if (src.Length != expected)
				{
				throw new InvalidOperationException (
					$"[MlTrainingUtils] Feature vector length mismatch. got={src.Length}, expected={expected}. " +
					"Это рассинхрон пайплайна: модель/ML schema ожидают фиксированную длину.");
				}

			var f = new float[expected];
			for (int i = 0; i < expected; i++)
				{
				var v = src[i];
				if (double.IsNaN (v) || double.IsInfinity (v))
					{
					throw new InvalidOperationException (
						$"[MlTrainingUtils] Non-finite feature at index {i}: {v}. " +
						"Это ошибка данных/индикаторов; такие значения ломают метрики и могут имитировать «утечки».");
					}

				f[i] = (float) v;
				}

			return f;
			}

		public static List<T> OversampleBinary<T> (
			IReadOnlyList<T> src,
			Func<T, bool> isPositive,
			Func<T, DateTime> dateSelector,
			double targetFrac )
			{
			if (src == null) throw new ArgumentNullException (nameof (src));
			if (isPositive == null) throw new ArgumentNullException (nameof (isPositive));
			if (dateSelector == null) throw new ArgumentNullException (nameof (dateSelector));
			if (targetFrac <= 0.0) throw new ArgumentOutOfRangeException (nameof (targetFrac));

			var pos = src.Where (isPositive).ToList ();
			var neg = src.Where (x => !isPositive (x)).ToList ();

			if (pos.Count == 0 || neg.Count == 0)
				return src.ToList ();

			bool posIsMajor = pos.Count >= neg.Count;
			int major = posIsMajor ? pos.Count : neg.Count;
			int minor = posIsMajor ? neg.Count : pos.Count;

			int target = (int) Math.Round (major * targetFrac, MidpointRounding.AwayFromZero);
			if (target <= minor)
				return src.ToList ();

			var minorList = posIsMajor ? neg : pos;

			var res = new List<T> (src.Count + (target - minor));
			res.AddRange (src);

			int need = target - minor;
			for (int i = 0; i < need; i++)
				res.Add (minorList[i % minorList.Count]);

			return res
				.OrderBy (dateSelector)
				.ToList ();
			}
		}
	}
