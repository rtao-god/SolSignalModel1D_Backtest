using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SolSignalModel1D_Backtest.Core.Data.Indicators
	{
	/// <summary>
	/// NDJSON-стор для дневных индикаторов вида:
	/// {"d":"YYYY-MM-DD","v":123.45}
	/// </summary>
	public sealed class IndicatorsNdjsonStore
		{
		private readonly string _path;
		public IndicatorsNdjsonStore ( string path ) { _path = path; }

		public sealed class IndicatorLine
			{
			public IndicatorLine ( DateTime dateUtc, double value )
				{
				D = dateUtc.Date;
				V = value;
				}
			public DateTime D { get; }
			public double V { get; }
			}

		public DateTime? TryGetLastDate ()
			{
			if (!File.Exists (_path)) return null;
			using var fs = new FileStream (_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (fs.Length == 0) return null;

			fs.Seek (-1, SeekOrigin.End);
			while (fs.Position > 0)
				{
				fs.Seek (-1, SeekOrigin.Current);
				if (fs.ReadByte () == '\n') break;
				fs.Seek (-1, SeekOrigin.Current);
				}
			using var sr = new StreamReader (fs, leaveOpen: true);
			string? last = sr.ReadLine ();
			if (string.IsNullOrWhiteSpace (last)) return null;

			try
				{
				var doc = JsonDocument.Parse (last);
				if (doc.RootElement.TryGetProperty ("d", out var dEl))
					{
					if (DateTime.TryParse (dEl.GetString (), out var d))
						return DateTime.SpecifyKind (d.Date, DateTimeKind.Utc);
					}
				}
			catch { }
			return null;
			}

		public void Append ( IEnumerable<IndicatorLine> lines )
			{
			Directory.CreateDirectory (Path.GetDirectoryName (_path)!);
			using var sw = new StreamWriter (_path, append: true);
			foreach (var l in lines)
				{
				var json = JsonSerializer.Serialize (new
					{
					d = l.D.ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture),
					v = l.V
					});
				sw.WriteLine (json);
				}
			}

		public Dictionary<DateTime, double> ReadRange ( DateTime startUtc, DateTime endUtc )
			{
			var res = new Dictionary<DateTime, double> ();
			if (!File.Exists (_path)) return res;

			using var fs = new FileStream (_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var sr = new StreamReader (fs);
			string? line;
			while ((line = sr.ReadLine ()) != null)
				{
				if (string.IsNullOrWhiteSpace (line)) continue;
				try
					{
					var doc = JsonDocument.Parse (line);
					var root = doc.RootElement;
					if (!root.TryGetProperty ("d", out var dEl)) continue;
					if (!root.TryGetProperty ("v", out var vEl)) continue;

					if (!DateTime.TryParse (dEl.GetString (), out var d)) continue;
					var date = DateTime.SpecifyKind (d.Date, DateTimeKind.Utc);
					if (date < startUtc.Date || date > endUtc.Date) continue;

					double v = vEl.GetDouble ();
					res[date] = v;
					}
				catch { /* skip bad rows */ }
				}
			return res;
			}
		}
	}
