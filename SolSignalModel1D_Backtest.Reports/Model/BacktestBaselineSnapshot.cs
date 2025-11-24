using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Reports.Model
	{
	/// <summary>
	/// Компактный снимок baseline-бэктеста без подробностей по сделкам.
	/// Используется для:
	/// - сохранения на диске;
	/// - отдачи через /api/backtest/baseline;
	/// - отображения в левой колонке UI (baseline vs preview).
	/// </summary>
	public sealed class BacktestBaselineSnapshot
		{
		/// <summary>
		/// Уникальный идентификатор снапшота.
		/// Например: backtest-baseline-20251123_013045.
		/// Генерируется в билдере.
		/// </summary>
		public string Id { get; init; } = string.Empty;

		/// <summary>
		/// Время генерации снапшота (UTC).
		/// Помогает выбирать "последний" результат.
		/// </summary>
		public DateTime GeneratedAtUtc { get; init; }

		/// <summary>
		/// Человекочитаемое имя конфига, по которому считался бэктест.
		/// Пока можно держать "default", в будущем — "baseline-v2" и т.п.
		/// </summary>
		public string ConfigName { get; init; } = "default";

		/// <summary>
		/// Глобальный дневной стоп (в долях, не в %).
		/// Например 0.05 = -5 %.
		/// </summary>
		public double DailyStopPct { get; init; }

		/// <summary>
		/// Глобальный дневной тейк-профит (в долях, не в %).
		/// Например 0.03 = +3 %.
		/// </summary>
		public double DailyTpPct { get; init; }

		/// <summary>
		/// Сводка по всем политикам и режимам (base / anti-direction).
		/// </summary>
		public IReadOnlyList<BacktestPolicySummary> Policies { get; init; }
			= Array.Empty<BacktestPolicySummary> ();
		}

	/// <summary>
	/// Сводка по одной политике в одном режиме (base или anti-direction overlay).
	/// Без сырых трейдов, только агрегированные метрики.
	/// </summary>
	public sealed class BacktestPolicySummary
		{
		/// <summary>
		/// Имя политики (например, "risk_aware", "const_3x").
		/// </summary>
		public string PolicyName { get; init; } = string.Empty;

		/// <summary>
		/// Режим маржи (Cross / Isolated).
		/// Хранится строкой для простого вывода на фронте.
		/// </summary>
		public string MarginMode { get; init; } = string.Empty;

		/// <summary>
		/// Признак того, что политика считалась с anti-direction overlay.
		/// false = базовый режим, true = overlay.
		/// </summary>
		public bool UseAntiDirectionOverlay { get; init; }

		/// <summary>
		/// Итоговый PnL в долях (например, 0.25 = +25 %).
		/// Берётся из BacktestPolicyResult.TotalPnlPct.
		/// </summary>
		public double TotalPnlPct { get; init; }

		/// <summary>
		/// Максимальная просадка в долях (например, -0.35 = -35 %).
		/// Берётся из BacktestPolicyResult.MaxDdPct.
		/// </summary>
		public double MaxDrawdownPct { get; init; }

		/// <summary>
		/// Был ли хотя бы один день с ликвидацией.
		/// </summary>
		public bool HadLiquidation { get; init; }

		/// <summary>
		/// Суммарно "вытащенные" средства (withdrawn) за всё время,
		/// в валюте счёта (USD).
		/// </summary>
		public double WithdrawnTotal { get; init; }

		/// <summary>
		/// Общее число сделок по данной политике и режиму.
		/// </summary>
		public int TradesCount { get; init; }
		}
	}
