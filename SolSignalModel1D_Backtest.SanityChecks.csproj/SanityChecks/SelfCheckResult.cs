using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.SanityChecks
	{
	/// <summary>
	/// Результат одного self-check'а:
	/// Success = true, если нет жёстких ошибок.
	/// Errors / Warnings = текстовые сообщения.
	/// Summary = краткое резюме для логов.
	/// Metrics = произвольные числовые метрики (acc, tpr и т.п.).
	/// </summary>
	public sealed class SelfCheckResult
		{
		/// <summary>Флаг общей успешности проверки.</summary>
		public bool Success { get; set; }

		/// <summary>Жёсткие ошибки (утечки, некорректные окна и т.п.).</summary>
		public List<string> Errors { get; } = new ();

		/// <summary>Мягкие предупреждения (подозрительная статистика, слабые метрики).</summary>
		public List<string> Warnings { get; } = new ();

		/// <summary>Краткое резюме для логов.</summary>
		public string Summary { get; set; } = string.Empty;

		/// <summary>
		/// Числовые метрики для проверки:
		/// плоский словарь вида "ключ" -> значение
		/// (например, "daily.acc_train", "sl.train.tpr").
		/// </summary>
		public Dictionary<string, double> Metrics { get; } = new ();

		public static SelfCheckResult Ok ( string summary )
			{
			return new SelfCheckResult
				{
				Success = true,
				Summary = summary
				};
			}

		public static SelfCheckResult Fail ( string summary, IEnumerable<string>? errors = null )
			{
			var res = new SelfCheckResult
				{
				Success = false,
				Summary = summary
				};

			if (errors != null)
				res.Errors.AddRange (errors);

			return res;
			}

		/// <summary>
		/// Агрегирует несколько результатов в один.
		/// Success = AND по всем Success; Errors/Warnings конкатенируются.
		/// Summary = конкатенация Summary через перевод строки.
		/// Metrics на этом уровне пока не агрегируются (оставляем пустыми).
		/// </summary>
		public static SelfCheckResult Aggregate ( IEnumerable<SelfCheckResult> results )
			{
			if (results == null) throw new ArgumentNullException (nameof (results));

			var agg = new SelfCheckResult
				{
				Success = true,
				Summary = "[aggregate]"
				};

			foreach (var r in results)
				{
				if (!r.Success)
					agg.Success = false;

				if (!string.IsNullOrWhiteSpace (r.Summary))
					agg.Summary += Environment.NewLine + r.Summary;

				agg.Errors.AddRange (r.Errors);
				agg.Warnings.AddRange (r.Warnings);
				}

			return agg;
			}
		}
	}
