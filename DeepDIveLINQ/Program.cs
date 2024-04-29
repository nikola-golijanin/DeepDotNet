using System.Collections;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;


//  Uncomment and run with dotnet run -c=Release to run benchmarks
// BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

IEnumerable<int> source = Enumerable.Range(0, 1000).ToArray();

//  Uncomment to check the types of Enumerators that are called
// Console.WriteLine(Test.SelectCompiler(source, i => i));
// Console.WriteLine(Test.SelectManual(source, i => i));
// Console.WriteLine(Enumerable.Select(source, i => i));

//  Uncomment to check that results are same for Linq Select(), SelectCompiler() and SelectManual(), SelectCompiler.WhereCompiler(), SelectManual().WhereManual()
// Console.WriteLine(Test.SelectCompiler(source, i => i).Sum());
// Console.WriteLine(Test.SelectManual(source, i => i).Sum());
// Console.WriteLine(Enumerable.Select(source, i => i).Sum());
// Console.WriteLine(Test.SelectManual(Test.WhereManual(source, i => i % 2 == 0), i => i * 2));
// Console.WriteLine(Test.SelectCompiler(Test.WhereCompiler(source, i => i % 2 == 0), i => i * 2).Sum());


// Uncomment to check that results are same for Linq Select.Where, SelectCompiler.WhereCompiler and SelectManual.WhereManual
// Console.WriteLine(Enumerable.Select(Enumerable.Where(source, i => i % 2 == 0), i => i * 2).Sum());
// Console.WriteLine(Test.SelectCompiler(Test.WhereCompiler(source, i => i % 2 == 0), i => i * 2).Sum());
// Console.WriteLine(Test.SelectManual(Test.WhereManual(source, i => i % 2 == 0), i => i * 2).Sum());
//



[MemoryDiagnoser]
[ShortRunJob]
public class Test
{
    private IEnumerable<int> source = Enumerable.Range(0, 1000).ToArray();

    #region Select implementations Benchmarks
    [Benchmark]
    public int SumCompiler()
    {
        int sum = 0;
        foreach (int i in SelectCompiler(source, i => i * 2))
        {
            sum += i;
        }
        return sum;
    }

    [Benchmark]
    public int SumManual()
    {
        int sum = 0;
        foreach (int i in SelectManual(source, i => i * 2))
        {
            sum += i;
        }
        return sum;
    }

    [Benchmark]
    public int SumLinq()
    {
        int sum = 0;

        // 1. Query expressions
        // IEnumerable<int> values = from i in source
        //                           where i % 2 == 0 
        //                           select i * 2;

        // 2. Extension methods
        // source.Where( i => i % 2 == 0)
        //       .Select(i => i * 2);

        // 3. Explicit static methods
        // Enumerable.Select(Enumerable.Where(source, i => i % 2 == 0), i => i * 2); 

        //All those 3 cases are functionally same thing


        foreach (int i in Enumerable.Select(Enumerable.Where(source, i => i % 2 == 0), i => i * 2))
        {
            sum += i;
        }
        return sum;
    }

    #endregion

    #region SelectCompiler and SelectManual implementation (SelectManual has also optimized ArrayImplementation and optimized implemntation if SelectManual().WhereManual() is chained)

    public static IEnumerable<TResult> SelectCompiler<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);


        if (source is TSource[] array)
        {
            return ArrayImpl(array, selector);
        }

        return EnumerableImpl(source, selector);



        static IEnumerable<TResult> EnumerableImpl(IEnumerable<TSource> source, Func<TSource, TResult> selector)
        {

            foreach (var item in source)
            {
                yield return selector(item);
            }
        }

        static IEnumerable<TResult> ArrayImpl(TSource[] source, Func<TSource, TResult> selector)
        {
            for (int i = 0; i < source.Length; i++)
            {
                yield return selector(source[i]);
            }

            //This for and foreach loops is same as for array because 
            //compiler creates pretty much same thing when we are talking about arrays 

            // foreach (var item in source)
            // {
            //     yield return selector(item);
            // }
        }
    }

    public static IEnumerable<TResult> SelectManual<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        //This if is to check wheather the source collection in SelectManual is is chained to WhereManual
        // if these two are chained in order SelectManual().WhereManual() then we can return optimized WhereSelectManualEnumerable instead of 
        // regular SelectManualEnumerable.
        // This is how linq usually works, it can infer types of chained operations and optimize enumerable(enumerator)
        // for concrete order of chained methods.
        // For example Select().Where() has implementation, Any(), Where().Select().OrderBy()
        // All these frequently used combinations have implemntations of their own enumerators.
        if(source is WhereManualEnumerable<TSource> where)
        {
            return new WhereSelectManualEnumerable<TSource,TResult>(where._source,where._filter,selector);
        }

        // The same story for chaining operator, here we can check wheater source is array and return 
        // enumerator optimized for with arrays 
        if (source is TSource[] array)
        {
            return new SelectManualArray<TSource, TResult>(array, selector);
        }

        return new SelectManualEnumerable<TSource, TResult>(source, selector);
    }

    sealed class SelectManualEnumerable<TSource, TResult> : IEnumerable<TResult>, IEnumerator<TResult>
    {
        private IEnumerable<TSource> _source;
        private Func<TSource, TResult> _selector;

        private TResult _current = default!;
        private IEnumerator<TSource>? _enumerator;
        private int _state = 0;

        private int _threadId = Environment.CurrentManagedThreadId;

        public SelectManualEnumerable(IEnumerable<TSource> source, Func<TSource, TResult> selector)
        {
            _source = source;
            _selector = selector;
        }

        public IEnumerator<TResult> GetEnumerator()
        {
            if (_threadId == Environment.CurrentManagedThreadId && _state == 0)
            {
                _state = 1;
                return this;
            }

            return new SelectManualEnumerable<TSource, TResult>(_source, _selector) { _state = 1 };
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        public TResult Current => _current;

        object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            switch (_state)
            {
                case 1:
                    _enumerator = _source.GetEnumerator();
                    _state = 2;
                    goto case 2;

                case 2:
                    Debug.Assert(_enumerator is not null);
                    try
                    {
                        if (_enumerator.MoveNext())
                        {
                            _current = _selector(_enumerator.Current);
                            return true;
                        }
                    }
                    catch
                    {
                        Dispose();
                        throw;
                    }
                    break;
            }

            Dispose();
            return false;
        }

        public void Dispose()
        {
            _state = -1;
            _enumerator?.Dispose();
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }
    }

    sealed class SelectManualArray<TSource, TResult> : IEnumerable<TResult>, IEnumerator<TResult>
    {
        private TSource[] _source;
        private Func<TSource, TResult> _selector;

        private TResult _current = default!;
        private int _state = 0;

        private int _threadId = Environment.CurrentManagedThreadId;

        public SelectManualArray(TSource[] source, Func<TSource, TResult> selector)
        {
            _source = source;
            _selector = selector;
        }

        public IEnumerator<TResult> GetEnumerator()
        {
            if (_threadId == Environment.CurrentManagedThreadId && _state == 0)
            {
                _state = 1;
                return this;
            }

            return new SelectManualArray<TSource, TResult>(_source, _selector) { _state = 1 };
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        public TResult Current => _current;

        object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            int i = _state - 1;
            TSource[] source = _source;

            if ((uint)i < (uint)_source.Length)
            {
                _state++;
                _current = _selector(_source[i]);
                return true;
            }

            Dispose();
            return false;
        }

        public void Dispose()
        {
            _state = -1;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }
    }

    #endregion

    #region WhereCompiler and WhereManual implementation

    public static IEnumerable<TSource> WhereCompiler<TSource>(IEnumerable<TSource> source, Func<TSource, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filter);

        return EnumerableImpl(source, filter);

        static IEnumerable<TSource> EnumerableImpl(IEnumerable<TSource> source, Func<TSource, bool> filter)
        {

            foreach (var item in source)
                if (filter(item))
                    yield return item;


        }
    }

    public static IEnumerable<TSource> WhereManual<TSource>(IEnumerable<TSource> source, Func<TSource, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filter);

        return new WhereManualEnumerable<TSource>(source, filter);
    }

    sealed class WhereManualEnumerable<TSource> : IEnumerable<TSource>, IEnumerator<TSource>
    {
        internal IEnumerable<TSource> _source;
        internal Func<TSource, bool> _filter;

        private TSource _current = default!;
        private IEnumerator<TSource>? _enumerator;
        private int _state = 0;

        private int _threadId = Environment.CurrentManagedThreadId;

        public WhereManualEnumerable(IEnumerable<TSource> source, Func<TSource, bool> filter)
        {
            _source = source;
            _filter = filter;
        }

        public IEnumerator<TSource> GetEnumerator()
        {
            if (_threadId == Environment.CurrentManagedThreadId && _state == 0)
            {
                _state = 1;
                return this;
            }

            return new WhereManualEnumerable<TSource>(_source, _filter) { _state = 1 };
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        public TSource Current => _current;

        object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            switch (_state)
            {
                case 1:
                    _enumerator = _source.GetEnumerator();
                    _state = 2;
                    goto case 2;

                case 2:
                    Debug.Assert(_enumerator is not null);
                    try
                    {
                        while (_enumerator.MoveNext())
                        {
                            TSource current = _enumerator.Current;
                            if (_filter(current))
                            {
                                _current = current;
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        Dispose();
                        throw;
                    }
                    break;
            }

            Dispose();
            return false;
        }

        public void Dispose()
        {
            _state = -1;
            _enumerator?.Dispose();
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }
    }

    #endregion

    #region WhereSelect implementation

    sealed class WhereSelectManualEnumerable<TSource, TResult> : IEnumerable<TResult>, IEnumerator<TResult>
    {
        private IEnumerable<TSource> _source;
        private Func<TSource, TResult> _selector;


        private Func<TSource, bool> _filter;
        private TResult _current = default!;
        private IEnumerator<TSource>? _enumerator;
        private int _state = 0;

        private int _threadId = Environment.CurrentManagedThreadId;

        public WhereSelectManualEnumerable(IEnumerable<TSource> source, Func<TSource, bool> filter, Func<TSource, TResult> selector)
        {
            _source = source;
            _filter = filter;
            _selector = selector;
        }

        public IEnumerator<TResult> GetEnumerator()
        {
            if (_threadId == Environment.CurrentManagedThreadId && _state == 0)
            {
                _state = 1;
                return this;
            }

            return new WhereSelectManualEnumerable<TSource, TResult>(_source, _filter, _selector) { _state = 1 };
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        public TResult Current => _current;

        object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            switch (_state)
            {
                case 1:
                    _enumerator = _source.GetEnumerator();
                    _state = 2;
                    goto case 2;

                case 2:
                    Debug.Assert(_enumerator is not null);
                    try
                    {
                        while (_enumerator.MoveNext())
                        {
                            TSource current = _enumerator.Current;
                            if (_filter(current))
                            {
                                _current = _selector(current);
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        Dispose();
                        throw;
                    }
                    break;
            }

            Dispose();
            return false;
        }

        public void Dispose()
        {
            _state = -1;
            _enumerator?.Dispose();
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }
    }

    #endregion
}
