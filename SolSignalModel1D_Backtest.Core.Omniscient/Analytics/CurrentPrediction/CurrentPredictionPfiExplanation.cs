using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.Analytics.ML;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics.CurrentPrediction
	{
	/// <summary>
	/// Маппер PFI (FeatureStats / FeatureImportanceSnapshot) → CurrentPredictionExplanationItem.
	/// Никакой новой математики: использует уже посчитанные PFI-метрики.
	/// </summary>
	public static class CurrentPredictionPfiExplanation
		{
		/// <summary>
		/// Добавляет в snapshot.ExplanationItems top-N фич из готового PFI-снимка.
		/// Когда topN <= 0 — берутся все фичи.
		/// </summary>
		public static void AppendTopFeaturesFromSnapshot (
			CurrentPredictionSnapshot snapshot,
			FeatureImportanceSnapshot pfiSnapshot,
			int topN = 0 )
			{
			if (snapshot == null) throw new ArgumentNullException (nameof (snapshot));
			if (pfiSnapshot == null) throw new ArgumentNullException (nameof (pfiSnapshot));

			AppendTopFeatures (
				snapshot: snapshot,
				stats: pfiSnapshot.Stats,
				modelTag: pfiSnapshot.Tag,
				topN: topN);
			}

		/// <summary>
		/// Добавляет в snapshot.ExplanationItems фичи по ImportanceAuc
		/// из списка FeatureStats. Модель идентифицируется тегом modelTag.
		/// topN > 0 ограничивает число фич; topN <= 0 — без ограничения (все фичи).
		/// </summary>
		public static void AppendTopFeatures (
			CurrentPredictionSnapshot snapshot,
			IReadOnlyList<FeatureStats> stats,
			string modelTag,
			int topN = 0 )
			{
			if (snapshot == null) throw new ArgumentNullException (nameof (snapshot));
			if (stats == null) throw new ArgumentNullException (nameof (stats));
			if (string.IsNullOrWhiteSpace (modelTag))
				throw new ArgumentException ("modelTag must be non-empty.", nameof (modelTag));
			if (stats.Count == 0)
				return;

			var orderedQuery = stats
				.Where (s => s != null)
				.OrderByDescending (s => s.ImportanceAuc);

			var ordered = (topN > 0
					? orderedQuery.Take (topN)
					: orderedQuery)
				.ToList ();

			if (ordered.Count == 0)
				return;

			// Продолжаем нумерацию рангов после уже добавленных explanation-элементов.
			int baseRank = snapshot.ExplanationItems.Count == 0
				? 1
				: snapshot.ExplanationItems.Max (e => e.Rank > 0 ? e.Rank : 0) + 1;

			int rank = baseRank;

			foreach (var fs in ordered)
				{
				string dirText;

				if (double.IsNaN (fs.MeanPos) || double.IsNaN (fs.MeanNeg))
					{
					dirText = "нет стабильной разницы между классами";
					}
				else if (fs.DeltaMean > 0)
					{
					dirText = "значение выше в положительном классе";
					}
				else if (fs.DeltaMean < 0)
					{
					dirText = "значение ниже в положительном классе";
					}
				else
					{
					dirText = "средние по классам примерно совпадают";
					}

				string desc =
					$"[{modelTag}] фича \"{fs.Name}\": " +
					$"|ΔAUC|={fs.ImportanceAuc:0.0003}, ΔAUC={fs.DeltaAuc:0.0003}, " +
					$"DeltaMean={fs.DeltaMean:0.0003} ({dirText}), " +
					$"corr(label)={fs.CorrLabel:0.0002}, corr(score)={fs.CorrScore:0.0002}, " +
					$"countPos={fs.CountPos}, countNeg={fs.CountNeg}.";

				var item = new CurrentPredictionExplanationItem
					{
					// Явно помечаем PFI, чтобы фронт мог отфильтровать/группировать.
					Kind = "pfi_feature",
					Name = fs.Name,
					Description = desc,

					// Value — основная "важность" для сортировки на фронте.
					Value = fs.ImportanceAuc,

					// Score зарезервирован под другие типы importance (SHAP и т.п.).
					Score = null,

					Rank = rank++
					};

				snapshot.ExplanationItems.Add (item);
				}
			}

		/// <summary>
		/// Утилита: берёт глобальные PFI-снимки и добавляет фичи по указанному тегу.
		/// Когда topN <= 0 — добавляются все фичи снимка.
		/// </summary>
		public static void AppendTopFeaturesFromGlobalSnapshots (
			CurrentPredictionSnapshot snapshot,
			string tagFilter,
			int topN = 0 )
			{
			if (snapshot == null) throw new ArgumentNullException (nameof (snapshot));
			if (string.IsNullOrWhiteSpace (tagFilter))
				throw new ArgumentException ("tagFilter must be non-empty.", nameof (tagFilter));

			var all = FeatureImportanceSnapshots.GetSnapshots ();
			if (all == null || all.Count == 0)
				return;

			// Берём последний по времени снимок с таким тегом (если теги уникальны — просто Single).
			var pfiSnapshot = all
				.Where (s => string.Equals (s.Tag, tagFilter, StringComparison.OrdinalIgnoreCase))
				.LastOrDefault ();

			if (pfiSnapshot == null)
				return;

			AppendTopFeaturesFromSnapshot (snapshot, pfiSnapshot, topN);
			}
		}
	}
