namespace SolSignalModel1D_Backtest.Core.Data.Windowing
{
	/// <summary>
	/// Полуоткрытый UTC-интервал [StartUtc; EndUtcExclusive).
	/// Нужен как типовой контракт, чтобы не плодить "<=/</+1m" по коду.
	/// </summary>
	public readonly struct UtcHalfOpenInterval
		{
		public DateTime StartUtc { get; }
		public DateTime EndUtcExclusive { get; }

		private UtcHalfOpenInterval ( DateTime startUtc, DateTime endUtcExclusive )
			{
			if (startUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("StartUtc must be UTC.", nameof (startUtc));
			if (endUtcExclusive.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("EndUtcExclusive must be UTC.", nameof (endUtcExclusive));
			if (endUtcExclusive <= startUtc)
				throw new ArgumentOutOfRangeException (nameof (endUtcExclusive), "EndUtcExclusive must be > StartUtc.");

			StartUtc = startUtc;
			EndUtcExclusive = endUtcExclusive;
			}

		public static UtcHalfOpenInterval Create ( DateTime startUtc, DateTime endUtcExclusive )
			=> new UtcHalfOpenInterval (startUtc, endUtcExclusive);
		}
	}
