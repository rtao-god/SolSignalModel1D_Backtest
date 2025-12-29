namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.ML.PFI
	{
	/// <summary>
	/// Один PFI-снимок по модели:
	/// - Tag (например, "train:move", "oos:dir-normal");
	/// - базовый AUC на eval-сете;
	/// - список статистик по фичам.
	/// </summary>
	public sealed class FeatureImportanceSnapshot
		{
		public string Tag { get; }
		public double BaselineAuc { get; }
		public IReadOnlyList<FeatureStats> Stats { get; }

		public FeatureImportanceSnapshot ( string tag, double baselineAuc, IReadOnlyList<FeatureStats> stats )
			{
			Tag = tag ?? throw new ArgumentNullException (nameof (tag));
			BaselineAuc = baselineAuc;
			Stats = stats ?? throw new ArgumentNullException (nameof (stats));
			}
		}

	/// <summary>
	/// Глобальное хранилище PFI-снимков за один прогон:
	/// туда пишет FeatureImportanceAnalyzer.LogBinaryFeatureImportance,
	/// а читает FeatureImportanceSummary.PrintGlobalSummary.
	/// </summary>
	public static class FeatureImportanceSnapshots
		{
		private static readonly object _sync = new object ();
		private static readonly List<FeatureImportanceSnapshot> _items = new List<FeatureImportanceSnapshot> ();

		/// <summary>
		/// Регистрирует новый снимок PFI по модели.
		/// Вызывается из FeatureImportanceAnalyzer.LogBinaryFeatureImportance.
		/// </summary>
		public static void RegisterSnapshot (
			string tag,
			double baselineAuc,
			IReadOnlyList<FeatureStats> stats )
			{
			if (tag == null) throw new ArgumentNullException (nameof (tag));
			if (stats == null) throw new ArgumentNullException (nameof (stats));

			lock (_sync)
				{
				// Делаем защитную копию списка, чтобы снаружи его потом не меняли.
				var copy = stats is List<FeatureStats> list
					? new List<FeatureStats> (list)
					: stats.ToList ();

				_items.Add (new FeatureImportanceSnapshot (tag, baselineAuc, copy));
				}
			}

		/// <summary>
		/// Возвращает снимки текущего прогона.
		/// Используется FeatureImportanceSummary.PrintGlobalSummary.
		/// </summary>
		public static IReadOnlyList<FeatureImportanceSnapshot> GetSnapshots ()
			{
			lock (_sync)
				{
				// Возвращаем копию массива, чтобы снаружи не могли модифицировать внутренний список.
				return _items.ToArray ();
				}
			}

		/// <summary>
		/// Очищает все накопленные снимки.
		/// Можно вызвать в начале новой сессии анализа, если нужно.
		/// Сейчас нигде не вызывается автоматически.
		/// </summary>
		public static void Clear ()
			{
			lock (_sync)
				{
				_items.Clear ();
				}
			}
		}
	}
