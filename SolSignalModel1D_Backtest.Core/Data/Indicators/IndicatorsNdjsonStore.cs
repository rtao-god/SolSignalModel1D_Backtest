using SolSignalModel1D_Backtest.Core.Utils.Time;
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
	///
	/// Контракт:
	/// - файл считается частью "истины" для ML: битые строки/непарсабельные даты — фатальны;
	/// - поддерживает атомарную перезапись (rebuild/backfill) без частично записанных файлов.
	/// </summary>
	public sealed class IndicatorsNdjsonStore ( string path )
		{
		private readonly string _path = path;

		public sealed class IndicatorLine ( DateTime dateUtc, double value )
			{
			public DateTime D { get; } = dateUtc.ToCausalDateUtc ();
			public double V { get; } = value;
			}

		/// <summary>
		/// Первая дата, встреченная в файле (по первой непустой строке).
		/// Нужна для backfill/rebuild: если требуемый старт раньше, чем storeFirst — надо перестраивать файл.
		/// </summary>
		public DateTime? TryGetFirstDate ()
			{
			if (!File.Exists (_path)) return null;

			using var fs = new FileStream (_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var sr = new StreamReader (fs);

			string? line;
			int lineIndex = 0;

			while ((line = sr.ReadLine ()) != null)
				{
				lineIndex++;
				if (string.IsNullOrWhiteSpace (line)) continue;

				return ParseLineDateOrThrow (line, lineIndex);
				}

			return null;
			}

		/// <summary>
		/// Последняя дата в файле (по последней непустой строке).
		/// Делаем O(n) проход по строкам: число дней небольшое, а корректность важнее трюков с Seek.
		/// </summary>
		public DateTime? TryGetLastDate ()
			{
			if (!File.Exists (_path)) return null;

			using var fs = new FileStream (_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var sr = new StreamReader (fs);

			string? line;
			int lineIndex = 0;

			string? lastNonEmpty = null;
			int lastIndex = 0;

			while ((line = sr.ReadLine ()) != null)
				{
				lineIndex++;
				if (string.IsNullOrWhiteSpace (line)) continue;

				lastNonEmpty = line;
				lastIndex = lineIndex;
				}

			if (lastNonEmpty == null) return null;
			return ParseLineDateOrThrow (lastNonEmpty, lastIndex);
			}

		/// <summary>
		/// Атомарная перезапись: пишем во временный файл, затем заменяем основной.
		/// Это защищает от частично записанного NDJSON при крэше/убийстве процесса.
		/// </summary>
		public void OverwriteAtomic ( IEnumerable<IndicatorLine> lines )
			{
			var dir = Path.GetDirectoryName (_path);
			if (!string.IsNullOrWhiteSpace (dir))
				Directory.CreateDirectory (dir);

			var tmp = _path + ".tmp." + Guid.NewGuid ().ToString ("N");

			try
				{
				using (var fs = new FileStream (tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				using (var sw = new StreamWriter (fs))
					{
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

				// Перемещение в пределах одного каталога обычно атомарно на уровне ФС.
				// Если файл существует — перезаписываем.
				File.Move (tmp, _path, overwrite: true);
				}
			catch
				{
				// На любой ошибке — пробуем удалить tmp, но исходный файл не трогаем.
				try { if (File.Exists (tmp)) File.Delete (tmp); } catch { /* ignore */ }
				throw;
				}
			}

		public void Append ( IEnumerable<IndicatorLine> lines )
			{
			var dir = Path.GetDirectoryName (_path);
			if (!string.IsNullOrWhiteSpace (dir))
				Directory.CreateDirectory (dir);

			using var fs = new FileStream (_path, FileMode.Append, FileAccess.Write, FileShare.Read);
			using var sw = new StreamWriter (fs);

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
		/// Читает словарь Date->value в диапазоне [startUtc..endUtc] (по causal-дням).
		/// Любая битая строка приводит к InvalidOperationException.
		/// </summary>
		public Dictionary<DateTime, double> ReadRange ( DateTime startUtc, DateTime endUtc )
			{
			var res = new Dictionary<DateTime, double> ();
			if (!File.Exists (_path)) return res;

			var start = startUtc.ToCausalDateUtc ();
			var end = endUtc.ToCausalDateUtc ();

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

					var date = d.ToCausalDateUtc ();
					if (date < start || date > end) continue;

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

		private DateTime ParseLineDateOrThrow ( string line, int lineIndex )
			{
			try
				{
				using var doc = JsonDocument.Parse (line);
				if (!doc.RootElement.TryGetProperty ("d", out var dEl))
					throw new InvalidOperationException (
						$"[indicators] property 'd' not found in '{_path}' at line #{lineIndex}: '{line}'");

				if (!DateTime.TryParse (dEl.GetString (), out var d))
					throw new InvalidOperationException (
						$"[indicators] cannot parse 'd' as DateTime in '{_path}' at line #{lineIndex}: '{line}'");

				return d.ToCausalDateUtc ();
				}
			catch (Exception ex)
				{
				throw new InvalidOperationException (
					$"[indicators] failed to parse NDJSON line in '{_path}' at line #{lineIndex}: '{line}'",
					ex);
				}
			}
		}
	}
