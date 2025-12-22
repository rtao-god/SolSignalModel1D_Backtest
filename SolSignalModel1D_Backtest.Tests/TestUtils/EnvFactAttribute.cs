using System;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.TestUtils
	{
	/// <summary>
	/// Условный Fact, который пропускает тест, если переменная окружения
	/// не установлена в ожидаемое значение.
	///
	/// Зачем так:
	/// - тяжелые/разрушительные E2E тесты нельзя запускать случайно;
	/// - пропуск должен быть именно Skip (а не Pass через return и не Fail).
	/// </summary>
	[AttributeUsage (AttributeTargets.Method, AllowMultiple = false)]
	public sealed class EnvFactAttribute : FactAttribute
		{
		public EnvFactAttribute ( string envVar, string expectedValue = "1", string? reason = null )
			{
			if (string.IsNullOrWhiteSpace (envVar))
				throw new ArgumentException ("envVar is null/empty", nameof (envVar));

			var v = Environment.GetEnvironmentVariable (envVar);

			if (!string.Equals (v, expectedValue, StringComparison.Ordinal))
				{
				Skip = reason ?? $"Set env {envVar}={expectedValue} to run this test.";
				}
			}
		}
	}
