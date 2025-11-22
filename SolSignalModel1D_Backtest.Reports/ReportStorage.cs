using SolSignalModel1D_Backtest.Reports.Model;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SolSignalModel1D_Backtest.Reports
	{
	/// <summary>
	/// Хранилище отчётов на файловой системе.
	/// Базовый каталог: {AppContext.BaseDirectory}/reports/{kind}/...
	/// </summary>
	public sealed class ReportStorage
		{
		private readonly string _baseDir;
		private readonly JsonSerializerOptions _jsonOptions;

		public ReportStorage ()
			{
			var root = AppContext.BaseDirectory;
			_baseDir = Path.Combine (root, "reports");
			Directory.CreateDirectory (_baseDir);

			_jsonOptions = new JsonSerializerOptions
				{
				WriteIndented = true
				};
			}

		private string GetKindDir ( string kind )
			{
			if (string.IsNullOrWhiteSpace (kind))
				throw new ArgumentException ("kind must be non-empty", nameof (kind));

			var dir = Path.Combine (_baseDir, kind);
			Directory.CreateDirectory (dir);
			return dir;
			}

		public void Save ( ReportDocument doc )
			{
			if (doc == null) throw new ArgumentNullException (nameof (doc));
			if (string.IsNullOrWhiteSpace (doc.Kind))
				throw new InvalidOperationException ("ReportDocument.Kind must be set before saving.");

			var dir = GetKindDir (doc.Kind);

			if (doc.GeneratedAtUtc == default)
				doc.GeneratedAtUtc = DateTime.UtcNow;

			if (string.IsNullOrWhiteSpace (doc.Id))
				doc.Id = $"{doc.Kind}-{doc.GeneratedAtUtc:yyyyMMdd-HHmmss}";

			var stamp = doc.GeneratedAtUtc.ToString ("yyyyMMdd-HHmmss");
			var fileName = $"{doc.Kind}-{stamp}.json";
			var path = Path.Combine (dir, fileName);

			var json = JsonSerializer.Serialize (doc, _jsonOptions);
			File.WriteAllText (path, json, Encoding.UTF8);
			}

		public ReportDocument? LoadLatest ( string kind )
			{
			var dir = GetKindDir (kind);
			if (!Directory.Exists (dir)) return null;

			var files = Directory.GetFiles (dir, $"{kind}-*.json", SearchOption.TopDirectoryOnly);
			if (files.Length == 0) return null;

			var latest = files.OrderBy (x => x).Last ();
			var json = File.ReadAllText (latest, Encoding.UTF8);
			return JsonSerializer.Deserialize<ReportDocument> (json, _jsonOptions);
			}
		}
	}
