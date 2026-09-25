using BenchmarkDotNet.Running;
using Pacpar.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(WarmBulkBenchmarks).Assembly).Run(args);
