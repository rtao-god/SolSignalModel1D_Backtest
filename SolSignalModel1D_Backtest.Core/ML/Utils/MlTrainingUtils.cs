using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Core.ML.Utils
	{
	/// <summary>
	/// Общие хелперы для подготовки данных к ML.NET:
	/// - строгая конвертация double[]/FeaturesVector -> float[] фиксированной длины;
	/// - oversample для бинарных задач без привязки к legacy DTO.
	/// </summary>
	public static class MlTrainingUtils
		{
		public static float[] ToFloatFixed ( ReadOnlyMemory<double> featuresVector )
			{
			var src = featuresVector.Span;
			int expected = CausalDataRow.FeatureCount;

			if (src.Length != expected)
				{
				throw new InvalidOperationException (
					$"[MlTrainingUtils] Feature vector length mismatch. got={src.Length}, expected={expected}. " +
					"Это значит, что код генерации фичей рассинхронизирован с CausalDataRow.FeatureNames.");
				}

			var f = new float[expected];
			for (int i = 0; i < expected; i++)
				f[i] = (float) src[i];

			return f;
			}

		/// <summary>
		/// Oversample бинарной задачи:
		/// - minor дублируется, пока его размер не достигнет major * targetFrac.
		/// - сортировка по времени сохраняет каузальный порядок.
		/// </summary>
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
