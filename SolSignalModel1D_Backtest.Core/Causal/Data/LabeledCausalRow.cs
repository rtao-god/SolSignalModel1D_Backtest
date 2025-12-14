namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	/// <summary>
	/// Единственный контракт, через который обучение видит истину.
	/// Инвариант: causal-часть отделена физически и остаётся иммутабельной.
	/// </summary>
	public sealed class LabeledCausalRow
		{
		public CausalDataRow Causal { get; }
		public int TrueLabel { get; }
		public bool FactMicroUp { get; }
		public bool FactMicroDown { get; }

		public DateTime DateUtc => Causal.DateUtc;

		public LabeledCausalRow ( CausalDataRow causal, int trueLabel, bool factMicroUp, bool factMicroDown )
			{
			Causal = causal ?? throw new ArgumentNullException (nameof (causal));

			if (trueLabel < 0 || trueLabel > 2)
				throw new ArgumentOutOfRangeException (nameof (trueLabel), trueLabel, "TrueLabel must be in [0..2].");

			if (factMicroUp && factMicroDown)
				throw new InvalidOperationException ("[LabeledCausalRow] FactMicroUp and FactMicroDown cannot be true одновременно.");

			TrueLabel = trueLabel;
			FactMicroUp = factMicroUp;
			FactMicroDown = factMicroDown;
			}
		}
	}
