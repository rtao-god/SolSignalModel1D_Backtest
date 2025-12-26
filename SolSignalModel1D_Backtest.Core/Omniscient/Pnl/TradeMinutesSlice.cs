using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using System;
using System.Collections;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Pnl
	{
	/// <summary>
	/// Лёгкий slice поверх IReadOnlyList без копирования.
	/// Нужен для delayed-окна: стартуем с произвольного индекса в baseline-минутках.
	/// </summary>
	internal readonly struct TradeMinutesSlice : IReadOnlyList<Candle1m>
		{
		private readonly IReadOnlyList<Candle1m> _source;
		private readonly int _startIndex;

		public TradeMinutesSlice ( IReadOnlyList<Candle1m> source, int startIndex )
			{
			_source = source ?? throw new ArgumentNullException (nameof (source));
			if (startIndex < 0 || startIndex >= source.Count)
				throw new ArgumentOutOfRangeException (nameof (startIndex));
			_startIndex = startIndex;
			}

		public int Count => _source.Count - _startIndex;

		public Candle1m this[int index]
			{
			get
				{
				if (index < 0 || index >= Count)
					throw new ArgumentOutOfRangeException (nameof (index));
				return _source[_startIndex + index];
				}
			}

		public IEnumerator<Candle1m> GetEnumerator ()
			{
			for (int i = _startIndex; i < _source.Count; i++)
				yield return _source[i];
			}

		IEnumerator IEnumerable.GetEnumerator () => GetEnumerator ();
		}
	}
