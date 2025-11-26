using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Reports.Model;
using System.Text.Json;

namespace SolSignalModel1D_Backtest.Reports.Backtest.Reports
	{
	/// <summary>
	/// Хранилище лёгких baseline-снапшотов бэктеста.
	/// Путь:
	///   {PathConfig.CacheRoot}/reports/backtest_baseline/{id}.json
	/// Пример:
	///   <repo>/cache/reports/backtest_baseline/backtest-baseline-20251123.json
	/// </summary>
	public sealed class BacktestBaselineStorage
		{
		private readonly string _dir;
		private readonly JsonSerializerOptions _jsonOptions;

		public BacktestBaselineStorage ()
			{
			// Корень кэша завязан на репу
			// PathConfig.CacheRoot = <repo>/cache
			_dir = Path.Combine (PathConfig.CacheRoot, "reports", "backtest_baseline");
			Directory.CreateDirectory (_dir);

			_jsonOptions = new JsonSerializerOptions
				{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				WriteIndented = true
				};
			}

		/// <summary>
		/// Сохраняет снапшот в {CacheRoot}/reports/backtest_baseline/{id}.json.
		/// </summary>
		public void Save ( BacktestBaselineSnapshot snapshot )
			{
			if (snapshot == null) throw new ArgumentNullException (nameof (snapshot));
			if (string.IsNullOrWhiteSpace (snapshot.Id))
				throw new InvalidOperationException ("BacktestBaselineSnapshot.Id must be non-empty.");

			var fileName = $"{snapshot.Id}.json";
			var fullPath = Path.Combine (_dir, fileName);

			var json = JsonSerializer.Serialize (snapshot, _jsonOptions);
			File.WriteAllText (fullPath, json);
			}

		/// <summary>
		/// Загружает последний по времени baseline-снапшот (по дате изменения файла).
		/// Если файлов нет — возвращает null.
		/// </summary>
		public BacktestBaselineSnapshot? LoadLatest ()
			{
			if (!Directory.Exists (_dir))
				return null;

			var latestFile = new DirectoryInfo (_dir)
				.EnumerateFiles ("*.json", SearchOption.TopDirectoryOnly)
				.OrderByDescending (f => f.LastWriteTimeUtc)
				.FirstOrDefault ();

			if (latestFile == null)
				return null;

			var json = File.ReadAllText (latestFile.FullName);
			return JsonSerializer.Deserialize<BacktestBaselineSnapshot> (json, _jsonOptions);
			}
		}
	}
