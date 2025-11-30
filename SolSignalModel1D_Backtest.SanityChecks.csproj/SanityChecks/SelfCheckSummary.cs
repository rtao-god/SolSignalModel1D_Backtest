namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks
	{
	/// <summary>
	/// Агрегированный результат запуска SelfCheckRunner.
	/// Success = все ли проверки прошли.
	/// Metrics = плоский словарь вида "{check}.{metric}".
	/// </summary>
	public sealed class SelfCheckSummary
		{
		public bool Success { get; }
		public IReadOnlyList<SanityCheckResult> Results { get; }
		public IReadOnlyDictionary<string, double> Metrics { get; }

		public SelfCheckSummary ( IReadOnlyList<SanityCheckResult> results )
			{
			Results = results ?? throw new ArgumentNullException (nameof (results));
			Success = Results.All (r => r.Success);

			var dict = new Dictionary<string, double> ();
			foreach (var r in Results)
				{
				foreach (var kv in r.Metrics)
					{
					var key = $"{r.CheckName}.{kv.Key}";
					dict[key] = kv.Value;
					}
				}

			Metrics = dict;
			}
		}
	}
