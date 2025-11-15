using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	/// <summary>
	/// NDJSON свечей: по строке на свечу, внизу самые свежие:
	/// {"t":"2025-11-11T12:00:00Z","o":..., "h":..., "l":..., "c":...}
	/// </summary>
	public sealed class CandleNdjsonStore
		{
		private readonly string _path;
		public CandleNdjsonStore ( string path ) { _path = path; }
		public string Path => _path;

		public DateTime? TryGetLastTimestampUtc ()
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
			string? lastLine = sr.ReadLine ();
			if (string.IsNullOrWhiteSpace (lastLine)) return null;

			try
				{
				var doc = JsonDocument.Parse (lastLine);
				if (doc.RootElement.TryGetProperty ("t", out var tEl))
					{
					var dt = DateTime.Parse (tEl.GetString ()!, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
					return dt;
					}
				}
			catch { }
			return null;
			}

		public void Append ( IEnumerable<CandleLine> candles )
			{
			Directory.CreateDirectory (System.IO.Path.GetDirectoryName (_path)!);
			using var sw = new StreamWriter (_path, append: true);
			foreach (var c in candles)
				{
				var line = JsonSerializer.Serialize (new
					{
					t = c.OpenTimeUtc.ToString ("o"),
					o = c.Open,
					h = c.High,
					l = c.Low,
					c = c.Close
					});
				sw.WriteLine (line);
				}
			}

		/// <summary>Чтение диапазона свечей (универсально для любого TF-файла).</summary>
		public List<CandleLine> ReadRange ( DateTime startUtc, DateTime endUtc )
			{
			var res = new List<CandleLine> ();
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

					if (!root.TryGetProperty ("t", out var tEl)) continue;
					var dt = DateTime.Parse (tEl.GetString ()!, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

					if (dt < startUtc || dt >= endUtc) continue;

					double o = root.GetProperty ("o").GetDouble ();
					double h = root.GetProperty ("h").GetDouble ();
					double l = root.GetProperty ("l").GetDouble ();
					double c = root.GetProperty ("c").GetDouble ();

					res.Add (new CandleLine (dt, o, h, l, c));
					}
				catch { /* skip bad rows */ }
				}
			res.Sort (( a, b ) => a.OpenTimeUtc.CompareTo (b.OpenTimeUtc));
			return res;
			}

		public readonly struct CandleLine
			{
			public CandleLine ( DateTime openTimeUtc, double open, double high, double low, double close )
				{
				OpenTimeUtc = openTimeUtc;
				Open = open; High = high; Low = low; Close = close;
				}
			public DateTime OpenTimeUtc { get; }
			public double Open { get; }
			public double High { get; }
			public double Low { get; }
			public double Close { get; }
			}
		}
	}
