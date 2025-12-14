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
	public sealed class IndicatorsNdjsonStore ( string path )
		{
		private readonly string _path = path;

		public sealed class IndicatorLine ( DateTime dateUtc, double value )
			{
			public DateTime D { get; } = dateUtc.Causal.DateUtc;
			public double V { get; } = value;
			}

		/// <summary>
		/// Возвращает дату последней строки в NDJSON.
		/// Любые проблемы с JSON/датой приводят к InvalidOperationException –
		/// это лучше, чем молча потерять хвост индикаторов.
		/// </summary>
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
				using var doc = JsonDocument.Parse (last);
				if (doc.RootElement.TryGetProperty ("d", out var dEl))
					{
					if (DateTime.TryParse (dEl.GetString (), out var d))
						return DateTime.SpecifyKind (d.Causal.DateUtc, DateTimeKind.Utc);
					}

				throw new InvalidOperationException (
					$"[indicators] invalid last NDJSON line in '{_path}': '{last}'");
				}
			catch (Exception ex)
				{
				throw new InvalidOperationException (
					$"[indicators] failed to parse last NDJSON line in '{_path}': '{last}'",
					ex);
				}
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

		/// <summary>
		/// Читает словарь Date->value в диапазоне [startUtc.Causal.DateUtc, endUtc.Causal.DateUtc].
		/// Любая битая строка приводит к InvalidOperationException – это как раз то,
		/// чего ты хочешь: не маскировать ошибки в кэше индикаторов.
		/// </summary>
		public Dictionary<DateTime, double> ReadRange ( DateTime startUtc, DateTime endUtc )
			{
			var res = new Dictionary<DateTime, double> ();
			if (!File.Exists (_path)) return res;

			using var fs = new FileStream (_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var sr = new StreamReader (fs);
			string? line;
			int lineIndex = 0;

			while ((line = sr.ReadLine ()) != null)
				{
				lineIndex++;
				if (string.IsNullOrWhiteSpace (line)) continue;
				try
					{
					using var doc = JsonDocument.Parse (line);
					var root = doc.RootElement;
					if (!root.TryGetProperty ("d", out var dEl))
						throw new InvalidOperationException (
							$"[indicators] property 'd' not found in '{_path}' at line #{lineIndex}: '{line}'");
					if (!root.TryGetProperty ("v", out var vEl))
						throw new InvalidOperationException (
							$"[indicators] property 'v' not found in '{_path}' at line #{lineIndex}: '{line}'");

					if (!DateTime.TryParse (dEl.GetString (), out var d))
						throw new InvalidOperationException (
							$"[indicators] cannot parse 'd' as DateTime in '{_path}' at line #{lineIndex}: '{line}'");

					var date = DateTime.SpecifyKind (d.Causal.DateUtc, DateTimeKind.Utc);
					if (date < startUtc.Causal.DateUtc || date > endUtc.Causal.DateUtc) continue;

					double v = vEl.GetDouble ();
					res[date] = v;
					}
				catch (Exception ex)
					{
					throw new InvalidOperationException (
						$"[indicators] invalid NDJSON in '{_path}' at line #{lineIndex}: '{line}'",
						ex);
					}
				}
			return res;
			}
		}
	}
