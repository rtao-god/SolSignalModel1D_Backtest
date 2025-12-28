using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.Candles
	{
	/// <summary>
	/// NDJSON свечей: по строке на свечу, внизу самые свежие:
	/// {"t":"2025-11-11T12:00:00Z","o":..., "h":..., "l":..., "c":...}
	/// Контракт:
	/// - каждая НЕпустая строка обязана быть валидным JSON;
	/// - обязателен ключ "t" + числовые "o/h/l/c";
	/// - время строго возрастает по файлу;
	/// - любая проблема в данных => InvalidOperationException с понятным текстом.
	/// </summary>
	public sealed class CandleNdjsonStore ( string path )
		{
		private readonly string _path = path;

		public string Path => _path;

		/// <summary>
		/// Краткий префикс строки для логируемых/исключений.
		/// Позволяет не засорять сообщение полным содержимым больших строк.
		/// </summary>
		private static string Preview ( string line ) =>
			line.Length <= 200 ? line : line.Substring (0, 200) + "...";

		/// <summary>
		/// Возвращает timestamp самой последней (самой свежей) свечи в файле.
		/// Если файл отсутствует или пуст — null.
		/// Любая битая строка / некорректный JSON → InvalidOperationException.
		/// Реализация: читаем файл целиком и берём последнюю непустую строку.
		/// </summary>
		public DateTime? TryGetLastTimestampUtc ()
			{
			if (!File.Exists (_path))
				return null;

			using var fs = new FileStream (
				_path,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite);

			if (fs.Length == 0)
				return null;

			using var sr = new StreamReader (fs);

			string? line;
			string? lastNonEmpty = null;
			int lineIndex = 0;
			int lastNonEmptyIndex = 0;

			// Идём по всему файлу и запоминаем последнюю НЕпустую строку.
			while ((line = sr.ReadLine ()) != null)
				{
				lineIndex++;
				if (string.IsNullOrWhiteSpace (line))
					continue;

				lastNonEmpty = line;
				lastNonEmptyIndex = lineIndex;
				}

			if (lastNonEmpty == null)
				return null; // файл состоит только из пустых строк

			try
				{
				using var doc = JsonDocument.Parse (lastNonEmpty);
				var root = doc.RootElement;

				if (!root.TryGetProperty ("t", out var tEl))
					{
					throw new InvalidOperationException (
						$"[candles] NDJSON schema violation in file '{_path}': " +
						$"property 't' not found in last line #{lastNonEmptyIndex}. " +
						$"Line='{Preview (lastNonEmpty)}'");
					}

				string? ts = tEl.GetString ();
				if (string.IsNullOrWhiteSpace (ts))
					{
					throw new InvalidOperationException (
						$"[candles] NDJSON schema violation in file '{_path}': " +
						$"property 't' is null/empty in last line #{lastNonEmptyIndex}. " +
						$"Line='{Preview (lastNonEmpty)}'");
					}

				var dt = DateTime.Parse (
					ts,
					null,
					DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

				return dt;
				}
			catch (JsonException ex)
				{
				throw new InvalidOperationException (
					$"[candles] Invalid JSON in last NDJSON line of '{_path}' " +
					$"(line #{lastNonEmptyIndex}). Line='{Preview (lastNonEmpty)}'",
					ex);
				}
			catch (FormatException ex)
				{
				throw new InvalidOperationException (
					$"[candles] Failed to parse 't' as DateTime in last NDJSON line of '{_path}' " +
					$"(line #{lastNonEmptyIndex}). Line='{Preview (lastNonEmpty)}'",
					ex);
				}
			}

		/// <summary>
		/// Возвращает timestamp самой первой (самой старой) свечи в файле.
		/// Если файл отсутствует или не удалось найти ни одной НЕпустой строки — null.
		/// Любая битая строка/контрактное нарушение => InvalidOperationException.
		/// </summary>
		public DateTime? TryGetFirstTimestampUtc ()
			{
			if (!File.Exists (_path))
				return null;

			using var fs = new FileStream (
				_path,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite);

			if (fs.Length == 0)
				return null;

			using var sr = new StreamReader (fs);
			string? line;
			int lineIndex = 0;

			while ((line = sr.ReadLine ()) != null)
				{
				lineIndex++;
				string raw = line;

				if (string.IsNullOrWhiteSpace (raw))
					continue;

				try
					{
					using var doc = JsonDocument.Parse (raw);
					var root = doc.RootElement;

					if (!root.TryGetProperty ("t", out var tEl))
						throw new InvalidOperationException (
							$"[candles] NDJSON schema violation in file '{_path}': property 't' not found " +
							$"in line #{lineIndex}. Line='{Preview (raw)}'");

					string? ts = tEl.GetString ();
					if (string.IsNullOrWhiteSpace (ts))
						throw new InvalidOperationException (
							$"[candles] NDJSON schema violation in file '{_path}': property 't' is null/empty " +
							$"in line #{lineIndex}. Line='{Preview (raw)}'");

					var dt = DateTime.Parse (
						ts,
						null,
						DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

					return dt;
					}
				catch (JsonException ex)
					{
					throw new InvalidOperationException (
						$"[candles] Invalid JSON in file '{_path}' at line #{lineIndex}. " +
						$"Line='{Preview (raw)}'",
						ex);
					}
				catch (FormatException ex)
					{
					throw new InvalidOperationException (
						$"[candles] Failed to parse 't' as DateTime in file '{_path}' at line #{lineIndex}. " +
						$"Line='{Preview (raw)}'",
						ex);
					}
				}

			return null;
			}

		/// <summary>
		/// Аппендит свечи в конец файла в NDJSON-формате.
		/// Каждая свеча записывается как одна JSON-строка + '\n'.
		/// Любые ошибки записи/сериализации не маскируются.
		/// </summary>
		public void Append ( IEnumerable<CandleLine> candles )
			{
			// Создаём директорию заранее, чтобы избежать неочевидных ошибок при открытии файла.
			Directory.CreateDirectory (System.IO.Path.GetDirectoryName (_path)!);

			// FileMode.Append: позиция всегда в конце файла.
			// FileShare.Read: другие потоки могут читать, но не записывать.
			using var fs = new FileStream (
				_path,
				FileMode.Append,
				FileAccess.Write,
				FileShare.Read);

			foreach (var c in candles)
				{
				// Формируем JSON-объект одной свечи.
				var json = JsonSerializer.Serialize (new
					{
					t = c.OpenTimeUtc.ToString ("o"),
					o = c.Open,
					h = c.High,
					l = c.Low,
					c = c.Close
					});

				// NDJSON: одна строка = один JSON + '\n'.
				var line = json + "\n";

				// Кодируем строку целиком в UTF-8 и пишем одним вызовом Write().
				var bytes = System.Text.Encoding.UTF8.GetBytes (line);
				fs.Write (bytes, 0, bytes.Length);
				}

			// Явный Flush с flushToDisk: минимизирует риск потери последних свечей при аварийном отключении.
			fs.Flush (flushToDisk: true);
			}

		/// <summary>
		/// Чтение диапазона свечей (универсально для любого TF-файла).
		/// Контракт:
		/// - файл отсортирован по времени по возрастанию;
		/// - время строго возрастает (dt_i &lt; dt_{i+1});
		/// - любые нарушения/битые строки → InvalidOperationException.
		/// </summary>
		public List<CandleLine> ReadRange ( DateTime startUtc, DateTime endUtc )
			{
			var res = new List<CandleLine> ();

			if (!File.Exists (_path))
				return res;

			startUtc = startUtc.ToUniversalTime ();
			endUtc = endUtc.ToUniversalTime ();

			if (startUtc >= endUtc)
				return res;

			using var fs = new FileStream (
				_path,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite);

			using var sr = new StreamReader (fs);
			string? line;
			int lineIndex = 0;
			DateTime? lastTime = null;

			while ((line = sr.ReadLine ()) != null)
				{
				lineIndex++;
				string raw = line;

				if (string.IsNullOrWhiteSpace (raw))
					continue;

				DateTime dt;
				double o, h, l, c;

				try
					{
					using var doc = JsonDocument.Parse (raw);
					var root = doc.RootElement;

					if (!root.TryGetProperty ("t", out var tEl))
						throw new InvalidOperationException (
							$"[candles] NDJSON schema violation in file '{_path}': property 't' not found " +
							$"in line #{lineIndex}. Line='{Preview (raw)}'");

					string? ts = tEl.GetString ();
					if (string.IsNullOrWhiteSpace (ts))
						throw new InvalidOperationException (
							$"[candles] NDJSON schema violation in file '{_path}': property 't' is null/empty " +
							$"in line #{lineIndex}. Line='{Preview (raw)}'");

					dt = DateTime.Parse (
						ts,
						null,
						DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

					// GetProperty бросит исключение, если ключа нет — это даёт явную ошибку по схеме.
					o = root.GetProperty ("o").GetDouble ();
					h = root.GetProperty ("h").GetDouble ();
					l = root.GetProperty ("l").GetDouble ();
					c = root.GetProperty ("c").GetDouble ();
					}
				catch (JsonException ex)
					{
					throw new InvalidOperationException (
						$"[candles] Invalid JSON in file '{_path}' at line #{lineIndex}. " +
						$"Line='{Preview (raw)}'",
						ex);
					}
				catch (FormatException ex)
					{
					throw new InvalidOperationException (
						$"[candles] Failed to parse date or numeric fields in file '{_path}' at line #{lineIndex}. " +
						$"Line='{Preview (raw)}'",
						ex);
					}
				catch (KeyNotFoundException ex)
					{
					throw new InvalidOperationException (
						$"[candles] NDJSON schema violation in file '{_path}': one of 'o/h/l/c' fields is missing " +
						$"in line #{lineIndex}. Line='{Preview (raw)}'",
						ex);
					}

				// Жёсткая проверка монотонности времени в файле.
				if (lastTime.HasValue && dt <= lastTime.Value)
					{
					throw new InvalidOperationException (
						$"[candles] Time is not strictly increasing in file '{_path}'. " +
						$"Line #{lineIndex} has timestamp {dt:O}, previous={lastTime:O}.");
					}

				lastTime = dt;

				if (dt < startUtc)
					continue;

				if (dt >= endUtc)
					break;

				res.Add (new CandleLine (dt, o, h, l, c));
				}

			return res;
			}

		public readonly struct CandleLine
			{
			public CandleLine ( DateTime openTimeUtc, double open, double high, double low, double close )
				{
				OpenTimeUtc = openTimeUtc;
				Open = open;
				High = high;
				Low = low;
				Close = close;
				}

			public DateTime OpenTimeUtc { get; }
			public double Open { get; }
			public double High { get; }
			public double Low { get; }
			public double Close { get; }
			}
		}
	}
