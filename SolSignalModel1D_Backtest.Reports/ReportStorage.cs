using System.Text.Json;
using System.Text.Json.Serialization;
using SolSignalModel1D_Backtest.Reports.Model;

namespace SolSignalModel1D_Backtest.Reports
	{
	/// <summary>
	/// Файловое хранилище отчётов.
	/// Структура:
	///   {RootDir}/{kind}/{id}.json
	/// где:
	///   kind = логический тип отчёта (например, "current_prediction", "backtest_summary", "backtest_baseline"),
	///   id   = уникальный идентификатор снапшота.
	/// </summary>
	public sealed class ReportStorage
		{
		private readonly string _rootDir;
		private readonly JsonSerializerOptions _jsonOptions;

		public ReportStorage ()
			{
			// Общий корень для ВСЕХ отчётов:
			//   <repo>/cache/reports
			// repo вычисляется внутри PathConfig.CacheRoot
			_rootDir = Path.Combine (
				Core.Infra.PathConfig.CacheRoot,
				"reports");

			Directory.CreateDirectory (_rootDir);

			_jsonOptions = new JsonSerializerOptions
				{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				WriteIndented = true,
				Converters =
		{
			new JsonStringEnumConverter()
		}
				};
			}

		/// <summary>
		/// Сохраняет ReportDocument как JSON.
		/// Путь: {root}/{kind}/{id}.json
		/// </summary>
		public void Save ( ReportDocument document )
			{
			if (document == null) throw new ArgumentNullException (nameof (document));
			if (string.IsNullOrWhiteSpace (document.Kind))
				throw new InvalidOperationException ("ReportDocument.Kind must be non-empty.");
			if (string.IsNullOrWhiteSpace (document.Id))
				throw new InvalidOperationException ("ReportDocument.Id must be non-empty.");

			var kindDir = GetKindDir (document.Kind);
			Directory.CreateDirectory (kindDir);

			var fileName = $"{document.Id}.json";
			var fullPath = Path.Combine (kindDir, fileName);

			var json = JsonSerializer.Serialize (document, _jsonOptions);
			File.WriteAllText (fullPath, json);
			}

		/// <summary>
		/// Загружает последний по времени current_prediction-отчёт.
		/// Используется /api/current-prediction.
		/// </summary>
		public ReportDocument? LoadLatestCurrentPrediction ()
			{
			return LoadLatestByKind ("current_prediction");
			}

		/// <summary>
		/// Загружает последний по времени backtest_summary-отчёт.
		/// Используется /api/backtest/summary.
		/// </summary>
		public ReportDocument? LoadLatestBacktestSummary ()
			{
			return LoadLatestByKind ("backtest_summary");
			}

		/// <summary>
		/// Общий метод для ReportDocument (legacy API).
		/// Оставлен ради обратной совместимости.
		/// </summary>
		public ReportDocument? LoadLatestByKind ( string kind )
			{
			return LoadLatest<ReportDocument> (kind);
			}

		public BacktestBaselineSnapshot? LoadLatestBacktestBaseline ()
			{
			const string kind = "backtest_baseline";

			var kindDir = GetKindDir (kind);
			if (!Directory.Exists (kindDir))
				return null;

			var files = Directory.GetFiles (kindDir, "*.json", SearchOption.TopDirectoryOnly);
			if (files.Length == 0)
				return null;

			var latestFile = files
				.Select (f => new FileInfo (f))
				.OrderByDescending (fi => fi.LastWriteTimeUtc)
				.First ();

			var json = File.ReadAllText (latestFile.FullName);
			return JsonSerializer.Deserialize<BacktestBaselineSnapshot> (json, _jsonOptions);
			}

		/// <summary>
		/// Загружает последний по времени отчёт с модельными статистиками бэктеста.
		/// Можно использовать для /api/backtest/model-stats
		/// </summary>
		public ReportDocument? LoadLatestBacktestModelStats ()
			{
			return LoadLatestByKind ("backtest_model_stats");
			}

		/// <summary>
		/// Универсальный загрузчик "последнего" JSON по kind для произвольного типа.
		/// Например:
		///   LoadLatest&lt;BacktestBaselineSnapshot&gt;("backtest_baseline")
		/// </summary>
		public T? LoadLatest<T> ( string kind )
			where T : class
			{
			if (string.IsNullOrWhiteSpace (kind))
				throw new ArgumentException ("Kind must be non-empty.", nameof (kind));

			var kindDir = GetKindDir (kind);
			if (!Directory.Exists (kindDir))
				return null;

			var files = Directory.GetFiles (kindDir, "*.json", SearchOption.TopDirectoryOnly);
			if (files.Length == 0)
				return null;

			// Берём последний по дате изменения файл.
			var latestFile = files
				.Select (f => new FileInfo (f))
				.OrderByDescending (fi => fi.LastWriteTimeUtc)
				.First ();

			var json = File.ReadAllText (latestFile.FullName);
			return JsonSerializer.Deserialize<T> (json, _jsonOptions);
			}

		private string GetKindDir ( string kind )
			{
			return Path.Combine (_rootDir, kind);
			}
		}
	}
