// Hand-written core types from System.Private.CoreLib
// These fundamental types cannot be generated via reflection since
// System.Private.CoreLib is the core runtime library.

// Branded numeric types (also emitted by generator in each file)
type int = number & { __brand: "int" };
type uint = number & { __brand: "uint" };
type byte = number & { __brand: "byte" };
type sbyte = number & { __brand: "sbyte" };
type short = number & { __brand: "short" };
type ushort = number & { __brand: "ushort" };
type long = number & { __brand: "long" };
type ulong = number & { __brand: "ulong" };
type float = number & { __brand: "float" };
type double = number & { __brand: "double" };
type decimal = number & { __brand: "decimal" };

declare namespace System {
  // Core exception types
  class Exception {
    constructor();
    constructor(message: string);
    constructor(message: string, innerException: Exception);

    readonly Message: string;
    readonly InnerException: Exception | null;
    readonly StackTrace: string | null;
    readonly Source: string | null;
    readonly HelpLink: string | null;

    ToString(): string;
  }

  class SystemException extends Exception {}
  class ArgumentException extends SystemException {}
  class ArgumentNullException extends ArgumentException {}
  class ArgumentOutOfRangeException extends ArgumentException {}
  class InvalidOperationException extends SystemException {}
  class NotSupportedException extends SystemException {}
  class NotImplementedException extends SystemException {}
  class ObjectDisposedException extends InvalidOperationException {}
  class IndexOutOfRangeException extends SystemException {}
  class NullReferenceException extends SystemException {}

  // Core interfaces
  interface IDisposable {
    Dispose(): void;
  }

  interface IEquatable<T> {
    Equals(other: T): boolean;
  }

  interface IComparable {
    CompareTo(obj: any): int;
  }

  interface IComparable<T> {
    CompareTo(other: T): int;
  }

  interface IFormattable {
    ToString(format: string | null, formatProvider: IFormatProvider | null): string;
  }

  interface IFormatProvider {
    GetFormat(formatType: Type): any | null;
  }

  // Core value types
  class TimeSpan {
    constructor(ticks: long);
    constructor(hours: int, minutes: int, seconds: int);
    constructor(days: int, hours: int, minutes: int, seconds: int);
    constructor(days: int, hours: int, minutes: int, seconds: int, milliseconds: int);

    readonly Days: int;
    readonly Hours: int;
    readonly Minutes: int;
    readonly Seconds: int;
    readonly Milliseconds: int;
    readonly Ticks: long;
    readonly TotalDays: double;
    readonly TotalHours: double;
    readonly TotalMinutes: double;
    readonly TotalSeconds: double;
    readonly TotalMilliseconds: double;

    static readonly Zero: TimeSpan;
    static readonly MaxValue: TimeSpan;
    static readonly MinValue: TimeSpan;

    static FromDays(value: double): TimeSpan;
    static FromHours(value: double): TimeSpan;
    static FromMinutes(value: double): TimeSpan;
    static FromSeconds(value: double): TimeSpan;
    static FromMilliseconds(value: double): TimeSpan;
    static FromTicks(value: long): TimeSpan;
  }

  class DateTime {
    constructor(ticks: long);
    constructor(year: int, month: int, day: int);
    constructor(year: int, month: int, day: int, hour: int, minute: int, second: int);

    readonly Year: int;
    readonly Month: int;
    readonly Day: int;
    readonly Hour: int;
    readonly Minute: int;
    readonly Second: int;
    readonly Millisecond: int;
    readonly Ticks: long;
    readonly DayOfWeek: DayOfWeek;
    readonly Date: DateTime;

    static readonly Now: DateTime;
    static readonly UtcNow: DateTime;
    static readonly Today: DateTime;
    static readonly MinValue: DateTime;
    static readonly MaxValue: DateTime;

    Add(value: TimeSpan): DateTime;
    AddDays(value: double): DateTime;
    AddHours(value: double): DateTime;
    AddMilliseconds(value: double): DateTime;
    AddMinutes(value: double): DateTime;
    AddMonths(months: int): DateTime;
    AddSeconds(value: double): DateTime;
    AddTicks(value: long): DateTime;
    AddYears(value: int): DateTime;
  }

  enum DayOfWeek {
    Sunday = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 3,
    Thursday = 4,
    Friday = 5,
    Saturday = 6
  }

  class Guid {
    constructor(b: ReadonlyArray<byte>);

    static readonly Empty: Guid;

    static NewGuid(): Guid;
    ToString(): string;
    static Parse(input: string): Guid;
    static TryParse(input: string, result: Guid): boolean;
  }

  class Type {
    readonly Name: string;
    readonly FullName: string | null;
    readonly Namespace: string | null;
    readonly Assembly: Reflection.Assembly;
    readonly IsGenericType: boolean;
    readonly IsClass: boolean;
    readonly IsInterface: boolean;
    readonly IsValueType: boolean;
    readonly IsEnum: boolean;
    readonly IsAbstract: boolean;
    readonly IsSealed: boolean;

    static GetType(typeName: string): Type | null;
  }

  class Delegate {
    readonly Method: Reflection.MethodInfo;
    readonly Target: any | null;
  }

  class MulticastDelegate extends Delegate {}

  // Core delegate types
  interface Action {
    (): void;
  }

  interface Action<T> {
    (arg: T): void;
  }

  interface Action<T1, T2> {
    (arg1: T1, arg2: T2): void;
  }

  interface Action<T1, T2, T3> {
    (arg1: T1, arg2: T2, arg3: T3): void;
  }

  interface Func<TResult> {
    (): TResult;
  }

  interface Func<T, TResult> {
    (arg: T): TResult;
  }

  interface Func<T1, T2, TResult> {
    (arg1: T1, arg2: T2): TResult;
  }

  interface Func<T1, T2, T3, TResult> {
    (arg1: T1, arg2: T2, arg3: T3): TResult;
  }

  interface Predicate<T> {
    (obj: T): boolean;
  }

  interface Comparison<T> {
    (x: T, y: T): int;
  }

  interface Converter<TInput, TOutput> {
    (input: TInput): TOutput;
  }

  class EventArgs {
    static readonly Empty: EventArgs;
  }

  interface EventHandler {
    (sender: any, e: EventArgs): void;
  }

  interface EventHandler<TEventArgs> {
    (sender: any, e: TEventArgs): void;
  }

  // Tuple types
  class Tuple<T1> {
    constructor(item1: T1);
    readonly Item1: T1;
  }

  class Tuple<T1, T2> {
    constructor(item1: T1, item2: T2);
    readonly Item1: T1;
    readonly Item2: T2;
  }

  // ValueTuple types
  class ValueTuple<T1> {
    constructor(item1: T1);
    Item1: T1;
  }

  class ValueTuple<T1, T2> {
    constructor(item1: T1, item2: T2);
    Item1: T1;
    Item2: T2;
  }
}

declare namespace System.Collections {
  interface IEnumerable {
    GetEnumerator(): IEnumerator;
  }

  interface IEnumerator {
    readonly Current: any;
    MoveNext(): boolean;
    Reset(): void;
  }

  interface ICollection extends IEnumerable {
    readonly Count: int;
    readonly IsSynchronized: boolean;
    readonly SyncRoot: any;
    CopyTo(array: any[], index: int): void;
  }

  interface IList extends ICollection {
    readonly Item: any;
    readonly IsReadOnly: boolean;
    readonly IsFixedSize: boolean;
    Add(value: any): int;
    Clear(): void;
    Contains(value: any): boolean;
    IndexOf(value: any): int;
    Insert(index: int, value: any): void;
    Remove(value: any): void;
    RemoveAt(index: int): void;
  }

  interface IDictionary extends ICollection {
    readonly Item: any;
    readonly Keys: ICollection;
    readonly Values: ICollection;
    readonly IsReadOnly: boolean;
    readonly IsFixedSize: boolean;
    Add(key: any, value: any): void;
    Clear(): void;
    Contains(key: any): boolean;
    Remove(key: any): void;
  }
}

declare namespace System.Collections.Generic {
  interface IEnumerable<T> extends System.Collections.IEnumerable {
    GetEnumerator(): IEnumerator<T>;
  }

  interface IEnumerator<T> extends System.Collections.IEnumerator {
    readonly Current: T;
  }

  interface ICollection<T> extends IEnumerable<T> {
    readonly Count: int;
    readonly IsReadOnly: boolean;
    Add(item: T): void;
    Clear(): void;
    Contains(item: T): boolean;
    CopyTo(array: T[], arrayIndex: int): void;
    Remove(item: T): boolean;
  }

  interface IList<T> extends ICollection<T> {
    readonly Item: T;
    IndexOf(item: T): int;
    Insert(index: int, item: T): void;
    RemoveAt(index: int): void;
  }

  interface IDictionary<TKey, TValue> extends ICollection<KeyValuePair<TKey, TValue>> {
    readonly Item: TValue;
    readonly Keys: ICollection<TKey>;
    readonly Values: ICollection<TValue>;
    Add(key: TKey, value: TValue): void;
    ContainsKey(key: TKey): boolean;
    Remove(key: TKey): boolean;
    TryGetValue(key: TKey, value: TValue): boolean;
  }

  class KeyValuePair<TKey, TValue> {
    constructor(key: TKey, value: TValue);
    readonly Key: TKey;
    readonly Value: TValue;
  }

  interface IComparer<T> {
    Compare(x: T, y: T): int;
  }

  interface IEqualityComparer<T> {
    Equals(x: T, y: T): boolean;
    GetHashCode(obj: T): int;
  }

  interface IReadOnlyCollection<T> extends IEnumerable<T> {
    readonly Count: int;
  }

  interface IReadOnlyList<T> extends IReadOnlyCollection<T> {
    readonly Item: T;
  }

  interface IReadOnlyDictionary<TKey, TValue> extends IReadOnlyCollection<KeyValuePair<TKey, TValue>> {
    readonly Item: TValue;
    readonly Keys: IEnumerable<TKey>;
    readonly Values: IEnumerable<TValue>;
    ContainsKey(key: TKey): boolean;
    TryGetValue(key: TKey, value: TValue): boolean;
  }

  interface IAsyncEnumerable<T> {
    GetAsyncEnumerator(cancellationToken: System.Threading.CancellationToken): IAsyncEnumerator<T>;
  }

  interface IAsyncEnumerator<T> extends System.IAsposable {
    readonly Current: T;
    MoveNextAsync(): System.Threading.Tasks.ValueTask<boolean>;
  }
}

declare namespace System.Threading {
  class CancellationToken {
    constructor(canceled: boolean);

    readonly IsCancellationRequested: boolean;
    readonly CanBeCanceled: boolean;
    readonly WaitHandle: WaitHandle;

    static readonly None: CancellationToken;

    ThrowIfCancellationRequested(): void;
  }

  class WaitHandle implements System.IDisposable {
    readonly SafeWaitHandle: any;

    WaitOne(): boolean;
    WaitOne(millisecondsTimeout: int): boolean;
    WaitOne(timeout: System.TimeSpan): boolean;

    Dispose(): void;
  }
}

declare namespace System.Threading.Tasks {
  class Task {
    readonly IsCompleted: boolean;
    readonly IsFaulted: boolean;
    readonly IsCanceled: boolean;
    readonly Exception: System.AggregateException | null;

    Wait(): void;
    Wait(millisecondsTimeout: int): boolean;
    Wait(timeout: System.TimeSpan): boolean;
    Wait(cancellationToken: System.Threading.CancellationToken): void;

    static Delay(millisecondsDelay: int): Task;
    static Delay(delay: System.TimeSpan): Task;
    static Run(action: System.Action): Task;
    static WhenAll(...tasks: Task[]): Task;
    static WhenAny(...tasks: Task[]): Task<Task>;
  }

  class Task<TResult> extends Task {
    readonly Result: TResult;

    static Run(function_: System.Func<TResult>): Task<TResult>;
  }

  class ValueTask<TResult> {
    constructor(result: TResult);
    constructor(task: Task<TResult>);

    readonly IsCompleted: boolean;
    readonly IsCompletedSuccessfully: boolean;
    readonly IsFaulted: boolean;
    readonly IsCanceled: boolean;
    readonly Result: TResult;

    AsTask(): Task<TResult>;
  }
}

declare namespace System.Runtime.Serialization {
  interface ISerializable {
    GetObjectData(info: SerializationInfo, context: StreamingContext): void;
  }

  class SerializationInfo {
    AddValue(name: string, value: any): void;
    GetValue(name: string, type: System.Type): any;
  }

  class StreamingContext {
    readonly State: StreamingContextStates;
    readonly Context: any;
  }

  enum StreamingContextStates {
    CrossProcess = 0x01,
    CrossMachine = 0x02,
    File = 0x04,
    Persistence = 0x08,
    Remoting = 0x10,
    Other = 0x20,
    Clone = 0x40,
    CrossAppDomain = 0x80,
    All = 0xFF
  }
}

declare namespace System.Reflection {
  class Assembly {
    readonly FullName: string | null;
    readonly Location: string;

    static Load(assemblyString: string): Assembly;
    static LoadFrom(assemblyFile: string): Assembly;

    GetTypes(): System.Type[];
    GetType(name: string): System.Type | null;
  }

  class MethodInfo {
    readonly Name: string;
    readonly ReturnType: System.Type;
    readonly IsStatic: boolean;
    readonly IsVirtual: boolean;
    readonly IsAbstract: boolean;

    Invoke(obj: any | null, parameters: any[] | null): any;
  }

  class PropertyInfo {
    readonly Name: string;
    readonly PropertyType: System.Type;
    readonly CanRead: boolean;
    readonly CanWrite: boolean;

    GetValue(obj: any | null): any;
    SetValue(obj: any | null, value: any): void;
  }
}
