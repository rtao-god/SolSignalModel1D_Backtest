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
	///   kind = ReportDocument.Kind (например, "current_prediction", "backtest_summary"),
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
		/// Сохраняет отчёт как JSON.
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

			// Для отладки: можно раскомментировать
			// Console.WriteLine($"[ReportStorage] Saved {document.Kind} -> {fullPath}");
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
		/// Используется /api/backtest/summary (на будущее).
		/// </summary>
		public ReportDocument? LoadLatestBacktestSummary ()
			{
			return LoadLatestByKind ("backtest_summary");
			}

		/// <summary>
		/// Общий метод: загружает последний по времени отчёт указанного kind.
		/// </summary>
		public ReportDocument? LoadLatestByKind ( string kind )
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
			return JsonSerializer.Deserialize<ReportDocument> (json, _jsonOptions);
			}

		private string GetKindDir ( string kind )
			{
			return Path.Combine (_rootDir, kind);
			}
		}
	}
