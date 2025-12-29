namespace SolSignalModel1D_Backtest.Core.Causal.Infra.Causality
	{
	/// <summary>
	/// Рантайм-защита от "future-peek".
	/// Идея: в каузальном участке кода задаётся asOfUtc, и любой выбор точки/свечи
	/// обязан гарантировать, что использованное время <= asOfUtc.
	/// Это не "лечит" утечку — это делает её воспроизводимой и локализуемой по стеку.
	/// </summary>
	public static class CausalityGuard
		{
		private sealed class Scope
			{
			public string Name = string.Empty;
			public DateTime AsOfUtc;
			public Scope? Prev;
			}

		private static readonly AsyncLocal<Scope?> CurrentScope = new AsyncLocal<Scope?> ();

		/// <summary>
		/// Глобальный флаг: включить/выключить проверки.
		/// Для поиска утечек включать, для обычных прогонов можно выключать.
		/// </summary>
		public static bool Enabled { get; set; } = true;

		/// <summary>
		/// Войти в каузальный scope: все проверки сравнивают usedUtc с asOfUtc.
		/// </summary>
		public static IDisposable Begin ( string name, DateTime asOfUtc )
			{
			if (!Enabled)
				return NoopDisposable.Instance;

			if (name == null) throw new ArgumentNullException (nameof (name));
			if (asOfUtc == default) throw new ArgumentException ("asOfUtc must be initialized.", nameof (asOfUtc));
			if (asOfUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("asOfUtc must be UTC (DateTimeKind.Utc).", nameof (asOfUtc));

			var prev = CurrentScope.Value;

			CurrentScope.Value = new Scope
				{
				Name = name,
				AsOfUtc = asOfUtc,
				Prev = prev
				};

			return new PopDisposable ();
			}

		/// <summary>
		/// Базовая проверка: использованное время не должно быть позже текущего asOfUtc.
		/// </summary>
		public static void AssertNotFuture ( DateTime usedUtc, string what )
			{
			if (!Enabled)
				return;

			var s = CurrentScope.Value;
			if (s == null)
				return;

			if (usedUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException (
					$"[causality] usedUtc must be UTC. scope={s.Name}, used={usedUtc:O}, kind={usedUtc.Kind}, what={what}");

			if (usedUtc > s.AsOfUtc)
				{
				throw new InvalidOperationException (
					$"[causality] FUTURE-PEEK detected. scope={s.Name}, asOf={s.AsOfUtc:O}, used={usedUtc:O}, what={what}");
				}
			}

		/// <summary>
		/// Проверка "свеча должна быть закрыта на момент asOfUtc".
		/// Например, 1h свеча с OpenTimeUtc=T считается известной целиком только после T+1h.
		/// </summary>
		public static void AssertCandleClosedAtOrBefore ( DateTime candleOpenUtc, TimeSpan tf, string what )
			{
			if (!Enabled)
				return;

			var s = CurrentScope.Value;
			if (s == null)
				return;

			var closeUtc = candleOpenUtc + tf;
			AssertNotFuture (closeUtc, what + $" (candleClose={closeUtc:O}, tf={tf})");
			}

		private sealed class PopDisposable : IDisposable
			{
			private int _disposed;

			public void Dispose ()
				{
				if (Interlocked.Exchange (ref _disposed, 1) != 0)
					return;

				var cur = CurrentScope.Value;
				CurrentScope.Value = cur?.Prev;
				}
			}

		private sealed class NoopDisposable : IDisposable
			{
			public static readonly NoopDisposable Instance = new NoopDisposable ();
			public void Dispose () { }
			}
		}
	}
