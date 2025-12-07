using System;
using System.IO;
using System.Linq;
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
			_rootDir = Path.Combine (
				Core.Infra.PathConfig.CacheRoot,
				"reports");

			Directory.CreateDirectory (_rootDir);

			_jsonOptions = new JsonSerializerOptions
				{
				// Используем camelCase для JSON-полей
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

				// Красивое форматирование
				WriteIndented = true,

				// разрешаем NaN/Infinity как именованные литералы ("NaN", "Infinity", "-Infinity")
				NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,

				// Enum'ы пишем как строки
				Converters =
				{
					new JsonStringEnumConverter()
				}
				};
			}

		/// <summary>
		/// Legacy-сохранение ReportDocument как JSON.
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
		/// Сохранение типизированного DTO без обёртки ReportDocument.
		/// kind/id управляются вызывающим кодом.
		/// </summary>
		public void SaveTyped<T> ( string kind, string id, T payload )
			where T : class
			{
			if (string.IsNullOrWhiteSpace (kind))
				throw new ArgumentException ("Kind must be non-empty.", nameof (kind));
			if (string.IsNullOrWhiteSpace (id))
				throw new ArgumentException ("Id must be non-empty.", nameof (id));
			if (payload == null) throw new ArgumentNullException (nameof (payload));

			var kindDir = GetKindDir (kind);
			Directory.CreateDirectory (kindDir);

			var fileName = $"{id}.json";
			var fullPath = Path.Combine (kindDir, fileName);

			var json = JsonSerializer.Serialize (payload, _jsonOptions);
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

		/// <summary>
		/// Загрузка типизированного DTO по kind/id.
		/// Например: LoadByKindAndId&lt;PolicyRatiosReportDto&gt;("policy_ratios", "baseline")
		/// </summary>
		public T? LoadByKindAndId<T> ( string kind, string id )
			where T : class
			{
			if (string.IsNullOrWhiteSpace (kind))
				throw new ArgumentException ("Kind must be non-empty.", nameof (kind));
			if (string.IsNullOrWhiteSpace (id))
				throw new ArgumentException ("Id must be non-empty.", nameof (id));

			var kindDir = GetKindDir (kind);
			if (!Directory.Exists (kindDir))
				return null;

			var fileName = $"{id}.json";
			var fullPath = Path.Combine (kindDir, fileName);
			if (!File.Exists (fullPath))
				return null;

			var json = File.ReadAllText (fullPath);
			return JsonSerializer.Deserialize<T> (json, _jsonOptions);
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

		public ReportDocument? LoadLatestBacktestModelStats ()
			{
			return LoadLatestByKind ("backtest_model_stats");
			}

		/// <summary>
		/// Универсальный загрузчик "последнего" JSON по kind для произвольного типа.
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
