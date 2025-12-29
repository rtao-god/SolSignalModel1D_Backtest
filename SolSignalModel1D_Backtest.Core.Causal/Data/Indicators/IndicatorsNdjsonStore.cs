using System.Globalization;
using System.Text.Json;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.Indicators
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

		private string IndicatorKey
			{
			get
				{
				var name = Path.GetFileNameWithoutExtension (_path);
				return string.IsNullOrWhiteSpace (name) ? "indicators" : name.Trim ().ToLowerInvariant ();
				}
			}

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

				var parsed = ParseLineOrThrow (line, lineIndex);
				return parsed.DateUtc;
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

			var parsedLast = ParseLineOrThrow (lastNonEmpty, lastIndex);
			return parsedLast.DateUtc;
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

				var parsed = ParseLineOrThrow (line, lineIndex);

				if (parsed.DateUtc < start || parsed.DateUtc > end)
					continue;

				res[parsed.DateUtc] = parsed.Value;
				}

			return res;
			}

		private (DateTime DateUtc, double Value) ParseLineOrThrow ( string rawLine, int lineIndex )
			{
			try
				{
				using var doc = JsonDocument.Parse (rawLine);
				var root = doc.RootElement;

				if (!root.TryGetProperty ("d", out var dEl))
					throw new InvalidOperationException (
						$"[indicators:{IndicatorKey}] property 'd' not found. source='{_path}', line=#{lineIndex}, raw='{rawLine}'");

				if (!root.TryGetProperty ("v", out var vEl))
					throw new InvalidOperationException (
						$"[indicators:{IndicatorKey}] property 'v' not found. source='{_path}', line=#{lineIndex}, raw='{rawLine}'");

				var dStr = dEl.GetString ();
				if (string.IsNullOrWhiteSpace (dStr))
					throw new InvalidOperationException (
						$"[indicators:{IndicatorKey}] empty 'd'. source='{_path}', line=#{lineIndex}, raw='{rawLine}'");

				if (!DateTime.TryParseExact (
						dStr,
						"yyyy-MM-dd",
						CultureInfo.InvariantCulture,
						DateTimeStyles.None,
						out var d))
					{
					throw new InvalidOperationException (
						$"[indicators:{IndicatorKey}] cannot parse day. day='{dStr}'. source='{_path}', line=#{lineIndex}, raw='{rawLine}'");
					}

				var day = d.ToCausalDateUtc ();

				// v может быть числом; если формат битый/тип не тот — GetDouble() кинет и уйдём в catch ниже.
				double v = vEl.GetDouble ();

				// Жёсткая защита от недопустимых double (в т.ч. если источник/генератор “впрыснул” NaN/Inf).
				if (double.IsNaN (v) || double.IsInfinity (v))
					{
					throw new InvalidOperationException (
						$"[indicators:{IndicatorKey}] invalid value parsed. day={day:yyyy-MM-dd}, v={v}. " +
						$"source='{_path}', line='{rawLine}'");
					}

				return (day, v);
				}
			catch (InvalidOperationException)
				{
				throw;
				}
			catch (Exception ex)
				{
				throw new InvalidOperationException (
					$"[indicators:{IndicatorKey}] invalid NDJSON. source='{_path}', line=#{lineIndex}, raw='{rawLine}'",
					ex);
				}
			}
		}
	}
