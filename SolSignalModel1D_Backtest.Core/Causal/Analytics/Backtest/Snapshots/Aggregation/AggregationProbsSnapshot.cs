using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Backtest.Snapshots.Aggregation
	{
	/// <summary>
	/// Snapshot агрегированных вероятностей по сегментам (Train/OOS/Recent/Full).
	/// Зачем нужен:
	/// - чтобы печатать понятный summary по вероятностям (средние P_up/P_flat/P_down),
	/// - чтобы иметь хвост последних дней для диагностики того, где реально вмешались overlay-слои (Micro/SL).
	/// </summary>
	public sealed class AggregationProbsSnapshot
		{
		/// <summary>
		/// Реальный диапазон дат входного набора rows (до split и исключений).
		/// Нужен, чтобы в выводе было видно покрытие бэктеста и не ловить “дырки” вслепую.
		/// </summary>
		public required DateTime MinDateUtc { get; init; }
		public required DateTime MaxDateUtc { get; init; }

		/// <summary>
		/// Сколько записей было на входе билдёра (до split на Train/OOS и до исключений).
		/// </summary>
		public required int TotalInputRecords { get; init; }

		/// <summary>
		/// Сколько дней/записей было исключено (например, из-за отсутствия baseline-exit).
		/// Эти записи не должны попадать в сегменты и метрики, иначе статистика будет “грязной”.
		/// </summary>
		public required int ExcludedCount { get; init; }

		/// <summary>
		/// Сегменты: Train, OOS, Recent, Full.
		/// </summary>
		public required IReadOnlyList<AggregationProbsSegmentSnapshot> Segments { get; init; }

		/// <summary>
		/// Хвост последних N дней для дебага наложений.
		/// Печатается отдельной таблицей и помогает быстро понять:
		/// - где micro реально изменил распределение,
		/// - где SL реально вмешался,
		/// - были ли “штрафы” long/short.
		/// </summary>
		public required IReadOnlyList<AggregationProbsDebugRow> DebugLastDays { get; init; }
		}

	/// <summary>
	/// Snapshot одного сегмента.
	/// Держит диапазон дат, количество дней и усреднённые вероятности по слоям.
	/// </summary>
	public sealed class AggregationProbsSegmentSnapshot
		{
		public required string SegmentName { get; init; }
		public required string SegmentLabel { get; init; }

		public required DateTime? FromDateUtc { get; init; }
		public required DateTime? ToDateUtc { get; init; }

		public required int RecordsCount { get; init; }

		/// <summary>
		/// Средние вероятности базового дневного слоя.
		/// </summary>
		public required AggregationLayerAvg Day { get; init; }

		/// <summary>
		/// Средние вероятности после micro-оверлея (Day+Micro).
		/// </summary>
		public required AggregationLayerAvg DayMicro { get; init; }

		/// <summary>
		/// Средние вероятности после SL-оверлея (Total = Day+Micro+SL).
		/// </summary>
		public required AggregationLayerAvg Total { get; init; }

		/// <summary>
		/// Средняя “уверенность” (как определена апстримом).
		/// Хранится отдельно от вероятностей: это другой сигнал, полезный для sanity-check.
		/// </summary>
		public required double AvgConfDay { get; init; }
		public required double AvgConfMicro { get; init; }

		/// <summary>
		/// Сколько дней имели ненулевой SL-score.
		/// Зачем: быстро понять, насколько часто SL вообще вмешивался в поток.
		/// </summary>
		public required int RecordsWithSlScore { get; init; }
		}

	/// <summary>
	/// Усреднённые вероятности одного слоя (up/flat/down) + средняя сумма.
	/// Sum намеренно хранится: если апстрим перестал нормировать в ~1.0, это видно сразу.
	/// </summary>
	public sealed class AggregationLayerAvg
		{
		public required double PUp { get; init; }
		public required double PFlat { get; init; }
		public required double PDown { get; init; }

		/// <summary>
		/// Среднее значение PUp+PFlat+PDown.
		/// Это не “должно быть строго 1.0”, но должно быть “разумно близко”.
		/// Если внезапно уехало к 0 — это индикатор деградации апстрима.
		/// </summary>
		public required double Sum { get; init; }
		}

	/// <summary>
	/// Тройка вероятностей для табличной печати и дебага.
	/// struct (readonly) — чтобы не плодить аллокации на хвосте debug-таблицы.
	/// </summary>
	public readonly struct TriProb
		{
		public double Up { get; }
		public double Flat { get; }
		public double Down { get; }

		public TriProb ( double up, double flat, double down )
			{
			Up = up;
			Flat = flat;
			Down = down;
			}
		}

	/// <summary>
	/// Одна строка “последних дней” для диагностики наложений.
	/// Эти флаги и поля напрямую используются принтером (AggregationProbsPrinter).
	/// </summary>
	public sealed class AggregationProbsDebugRow
		{
		public required DateTime DateUtc { get; init; }
		public required int TrueLabel { get; init; }

		public required int PredDay { get; init; }
		public required int PredDayMicro { get; init; }
		public required int PredTotal { get; init; }

		public required TriProb PDay { get; init; }
		public required TriProb PDayMicro { get; init; }
		public required TriProb PTotal { get; init; }

		/// <summary>
		/// Было ли реальное изменение Day -> DayMicro (по eps в билдере).
		/// </summary>
		public required bool MicroUsed { get; init; }

		/// <summary>
		/// Было ли реальное изменение DayMicro -> Total (SL) или явный сигнал SL.
		/// </summary>
		public required bool SlUsed { get; init; }

		/// <summary>
		/// Совпали ли метки (классы) Day и DayMicro.
		/// </summary>
		public required bool MicroAgree { get; init; }

		/// <summary>
		/// “Штраф” long: Total уменьшил P_up относительно DayMicro (по eps).
		/// </summary>
		public required bool SlPenLong { get; init; }

		/// <summary>
		/// “Штраф” short: Total уменьшил P_down относительно DayMicro (по eps).
		/// </summary>
		public required bool SlPenShort { get; init; }
		}
	}
