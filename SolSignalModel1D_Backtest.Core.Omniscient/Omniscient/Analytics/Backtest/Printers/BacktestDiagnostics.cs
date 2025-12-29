namespace SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Analytics.Backtest.Printers
	{
	/// <summary>
	/// Сборник варнингов/ошибок, чтобы в конце бэктеста всё увидеть в одном месте.
	/// </summary>
	public sealed class BacktestDiagnostics
		{
		private readonly List<string> _messages = new List<string> ();

		public bool HasMessages => _messages.Count > 0;

		public void Add ( string msg )
			{
			if (!string.IsNullOrWhiteSpace (msg))
				_messages.Add (msg);
			}

		public void AddMissing1h ( DateTime dateUtc )
			{
			_messages.Add ($"[diag] no 1h candles for day {dateUtc:yyyy-MM-dd} — intraday/liq checks are approximate.");
			}

		public void AddBadRecord ( DateTime dateUtc, string reason )
			{
			_messages.Add ($"[diag] record {dateUtc:yyyy-MM-dd}: {reason}");
			}

		public void Print ()
			{
			if (_messages.Count == 0)
				{
				Console.WriteLine ("[diag] no problems detected.");
				return;
				}

			Console.WriteLine ();
			Console.WriteLine ("==== DIAGNOSTICS ====");
			foreach (var m in _messages)
				Console.WriteLine (m);
			}
		}
	}
