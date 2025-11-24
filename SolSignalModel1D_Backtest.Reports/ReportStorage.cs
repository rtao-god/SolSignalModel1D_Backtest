using System;
using System.Collections.Generic;
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
	///   kind = тип отчёта (например, "current_prediction", "backtest_summary", "backtest_baseline"),
	///   id   = уникальный идентификатор снапшота.
	/// </summary>
	public sealed class ReportStorage
		{
		private readonly string _rootDir;
		private readonly JsonSerializerOptions _jsonOptions;

		public ReportStorage ()
			{
			// Общий корень для ВСЕХ приложений в рамках пользователя:
			//   %LOCALAPPDATA%\SolSignalModel1D_Backtest\reports
			var baseDir = Path.Combine (
				Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData),
				"SolSignalModel1D_Backtest",
				"reports");

			_rootDir = baseDir;
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
		/// Сохраняет отчёт типа ReportDocument как JSON.
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
		/// Сохраняет произвольный DTO с Id (например, BacktestBaselineSnapshot).
		/// Путь: {root}/{kind}/{id}.json, где:
		///   kind = произвольная строка (например, "backtest_baseline"),
		///   Id берётся из свойства payload.Id.
		/// </summary>
		public void Save<T> ( T payload, string kind )
			where T : class
			{
			if (payload == null) throw new ArgumentNullException (nameof (payload));
			if (string.IsNullOrWhiteSpace (kind))
				throw new ArgumentException ("Kind must be non-empty.", nameof (kind));

			var id = TryGetId (payload);
			if (string.IsNullOrWhiteSpace (id))
				{
				throw new InvalidOperationException (
					$"Payload type {typeof (T).Name} must have non-empty string Id property.");
				}

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
			return LoadLatest<ReportDocument> ("current_prediction");
			}

		/// <summary>
		/// Загружает последний по времени backtest_summary-отчёт.
		/// Используется /api/backtest/summary (на будущее).
		/// </summary>
		public ReportDocument? LoadLatestBacktestSummary ()
			{
			return LoadLatest<ReportDocument> ("backtest_summary");
			}

		/// <summary>
		/// Общий generic-метод: загружает последний по времени отчёт указанного kind.
		/// Тип T определяет схему JSON (ReportDocument, BacktestBaselineSnapshot и т.п.).
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

		/// <summary>
		/// Старая совместимая обёртка для ReportDocument.
		/// </summary>
		public ReportDocument? LoadLatestByKind ( string kind )
			{
			return LoadLatest<ReportDocument> (kind);
			}

		private string GetKindDir ( string kind )
			{
			return Path.Combine (_rootDir, kind);
			}

		/// <summary>
		/// Пытается прочитать строковое свойство Id у произвольного DTO.
		/// Используется generic-Save для "жёстких" DTO.
		/// </summary>
		private static string? TryGetId ( object payload )
			{
			var type = payload.GetType ();
			var prop = type.GetProperty ("Id");
			if (prop == null || prop.PropertyType != typeof (string))
				return null;

			return (string?) prop.GetValue (payload);
			}
		}
	}
