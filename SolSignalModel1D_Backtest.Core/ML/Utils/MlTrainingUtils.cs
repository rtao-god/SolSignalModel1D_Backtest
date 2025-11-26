using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.ML.Utils
	{
	/// <summary>
	/// Общие хелперы для подготовки данных к ML.NET:
	/// - конвертация double[] → float[] фиксированной длины;
	/// - простой oversample для бинарных задач по DataRow.
	/// </summary>
	public static class MlTrainingUtils
		{
		/// <summary>
		/// Конвертирует массив double в float[], обрезая/заполняя до MlSchema.FeatureCount.
		/// Это гарантирует стабильный размер фичей под ML.NET.
		/// </summary>
		public static float[] ToFloatFixed ( double[] src )
			{
			var f = new float[MlSchema.FeatureCount];

			if (src == null || src.Length == 0)
				return f;

			int len = Math.Min (src.Length, MlSchema.FeatureCount);
			for (int i = 0; i < len; i++)
				f[i] = (float) src[i];

			return f;
			}

		/// <summary>
		/// Простое oversample для бинарной задачи на DataRow:
		/// - major = более частый класс,
		/// - minor = более редкий класс,
		/// - minor дублируется, пока его размер не достигнет major * targetFrac.
		/// Порядок по дате сохраняется.
		/// </summary>
		public static List<DataRow> OversampleBinary (
			List<DataRow> src,
			Func<DataRow, bool> isPositive,
			double targetFrac )
			{
			if (src == null) throw new ArgumentNullException (nameof (src));
			if (isPositive == null) throw new ArgumentNullException (nameof (isPositive));

			var pos = src.Where (isPositive).ToList ();
			var neg = src.Where (r => !isPositive (r)).ToList ();

			// если какой-то класс пустой — ничего не делаем, чтобы не плодить мусор
			if (pos.Count == 0 || neg.Count == 0)
				return src;

			bool posIsMajor = pos.Count >= neg.Count;
			int major = posIsMajor ? pos.Count : neg.Count;
			int minor = posIsMajor ? neg.Count : pos.Count;

			int target = (int) Math.Round (major * targetFrac, MidpointRounding.AwayFromZero);
			if (target <= minor)
				return src;

			var minorList = posIsMajor ? neg : pos;

			var res = new List<DataRow> (src.Count + (target - minor));
			res.AddRange (src);

			int need = target - minor;
			for (int i = 0; i < need; i++)
				res.Add (minorList[i % minorList.Count]);

			// сортировка по времени, чтобы сохранить каузальный порядок
			return res
				.OrderBy (r => r.Date)
				.ToList ();
			}
		}
	}
