namespace SolSignalModel1D_Backtest.Core.Causal.Data.Candles
	{
	/// <summary>
	/// Централизованный аудит файловых операций по свечам.
	/// Цель: если файл удалён — в логе должна быть 1 строка:
	/// что удалили, почему удалили, и краткая инфа о файле (размер + first timestamp).
	/// </summary>
	public static class CandleFsAudit
		{
		/// <summary>
		/// Короткий tag запуска (например, первые 8 символов Guid),
		/// чтобы связывать строки логов одного старта.
		/// </summary>
		public static string RunTag { get; set; } = string.Empty;

		private static string TagPrefix =>
			string.IsNullOrWhiteSpace (RunTag) ? string.Empty : $"[{RunTag}]";

		/// <summary>
		/// Удаление файла с 1 компактной строкой в консоль.
		/// Если файла нет — молча выходим (без спама).
		/// </summary>
		public static void Delete ( string path, string reason )
			{
			if (string.IsNullOrWhiteSpace (path))
				throw new ArgumentException ("path is null/empty", nameof (path));

			try
				{
				if (!File.Exists (path))
					return;

				var fi = new FileInfo (path);
				long bytes = fi.Length;

				DateTime? first = null;
				string firstStr;

				try
					{
					first = new CandleNdjsonStore (path).TryGetFirstTimestampUtc ();
					firstStr = first.HasValue ? first.Value.ToString ("O") : "null";
					}
				catch (Exception ex)
					{
					// Ошибку чтения first не маскируем — фиксируем тип в логе.
					firstStr = "err:" + ex.GetType ().Name;
					}

				Console.WriteLine (
					$"[candles-fs]{TagPrefix} delete file='{Path.GetFileName (path)}' bytes={bytes} first={firstStr} reason='{reason}'");

				File.Delete (path);
				}
			catch (Exception ex)
				{
				throw new IOException (
					$"[candles-fs]{TagPrefix} delete failed path='{path}' reason='{reason}'",
					ex);
				}
			}
		}
	}
