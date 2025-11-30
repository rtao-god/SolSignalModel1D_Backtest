using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks
	{
	/// <summary>
	/// Результат набора self-check'ов.
	/// Success = true, если нет жёстких ошибок.
	/// </summary>
	public sealed class SelfCheckResult
		{
		public bool Success { get; init; }

		/// <summary>Жёсткие ошибки (утечка, некорректные окна и т.п.).</summary>
		public List<string> Errors { get; } = new ();

		/// <summary>Мягкие предупреждения (подозрительная статистика, слабые метрики).</summary>
		public List<string> Warnings { get; } = new ();

		/// <summary>Краткое резюме для логов.</summary>
		public string Summary { get; init; } = string.Empty;

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
		/// Агрегирует несколько результатов.
		/// </summary>
		public static SelfCheckResult Aggregate ( IEnumerable<SelfCheckResult> results )
			{
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
