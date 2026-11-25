using Xunit;

// WPF's Application.Current is AppDomain-wide. The window smoke tests each
// create an Application on an STA thread when Current is null; running them
// in parallel lets both observe null and construct a second instance, which
// WPF forbids. Serializing collections keeps the single-instance invariant.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
