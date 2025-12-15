namespace SolSignalModel1D_Backtest.Core.Causal.Features
	{
	public interface IFeatureBuilder
		{
		void Build ( FeatureContext ctx );
		}

	public sealed class FeatureContext
		{
		public BacktestRecord Row { get; }
		public List<double> Features { get; }

		public FeatureContext ( BacktestRecord row )
			{
			Row = row ?? throw new ArgumentNullException (nameof (row));
			Features = new List<double> (64);
			}

		public void Add ( double v )
			{
			if (double.IsNaN (v) || double.IsInfinity (v))
				throw new InvalidOperationException (
					$"[features] non-finite value for {Row.DateUtc:O}: {v}");

			Features.Add (v);
			}

		public void Add ( double? v )
			{
			if (v is null)
				throw new InvalidOperationException (
					$"[features] missing value for {Row.DateUtc:O}");

			Add (v.Value);
			}

		public void Add01 ( bool v )
			{
			Add (v ? 1.0 : 0.0);
			}

		public void Add01 ( bool? v )
			{
			if (v is null)
				throw new InvalidOperationException (
					$"[features] missing bool for {Row.DateUtc:O}");

			Add01 (v.Value);
			}
		}

	public sealed class FeaturePipeline
		{
		private readonly IFeatureBuilder[] _builders;

		public FeaturePipeline ( params IFeatureBuilder[] builders )
			{
			_builders = builders ?? Array.Empty<IFeatureBuilder> ();
			}

		public void Run ( FeatureContext ctx )
			{
			if (ctx == null) throw new ArgumentNullException (nameof (ctx));

			foreach (var b in _builders)
				b.Build (ctx);
			}
		}
	}
