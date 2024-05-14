using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

int i = 42;
var span = new MySpan<int>(ref i);
span[0] = 53;
Console.WriteLine(i);


MySpan<char> span2 = new MySpan<char>("Hello world".ToCharArray());
while (span2.Length > 0)
{
    Console.WriteLine(span2[0]);
    span2 = span2.Slice(1);
}

// Console.WriteLine(i);

readonly ref struct MySpan<T>
{
    private readonly ref T _reference;
    private readonly int _length;

    public int Length => _length;


    public MySpan(T[] array)
    {
        ArgumentNullException.ThrowIfNull(array);

        if (!typeof(T).IsValueType && array.GetType() != typeof(T[]))
        {
            throw new ArgumentException();
        }

        _reference = ref MemoryMarshal.GetArrayDataReference(array);
        _length = array.Length;
    }

    public MySpan(ref T reference)
    {
        _reference = ref reference;
        _length = 1;
    }

    public MySpan(ref T reference, int length)
    {
        _reference = ref reference;
        _length = length;
    }

    public ref T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_length)
            {
                throw new IndexOutOfRangeException();
            }
            return ref Unsafe.Add(ref _reference, index);
        }
    }

    public MySpan<T> Slice(int offset)
    {
        if ((uint)offset > (uint)_length)
        {
            throw new ArgumentOutOfRangeException();
        }

        return new MySpan<T>(ref Unsafe.Add(ref _reference, offset), _length - offset);
    }
}


readonly ref struct MyReadOnlySpan<T>
{
    private readonly ref T _reference;
    private readonly int _length;

    public int Length => _length;


    public MyReadOnlySpan(T[] array)
    {
        ArgumentNullException.ThrowIfNull(array);

        if (!typeof(T).IsValueType && array.GetType() != typeof(T[]))
        {
            throw new ArgumentException();
        }

        _reference = ref MemoryMarshal.GetArrayDataReference(array);
        _length = array.Length;
    }

    public MyReadOnlySpan(ref T reference)
    {
        _reference = ref reference;
        _length = 1;
    }

    public MyReadOnlySpan(ref T reference, int length)
    {
        _reference = ref reference;
        _length = length;
    }

    public ref readonly T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_length)
            {
                throw new IndexOutOfRangeException();
            }
            return ref Unsafe.Add(ref _reference, index);
        }
    }

    public MyReadOnlySpan<T> Slice(int offset)
    {
        if ((uint)offset > (uint)_length)
        {
            throw new ArgumentOutOfRangeException();
        }

        return new MyReadOnlySpan<T>(ref Unsafe.Add(ref _reference, offset), _length - offset);
    }
}