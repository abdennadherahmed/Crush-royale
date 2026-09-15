namespace CrushRoyale.Core.Common
{
    /// <summary>
    /// Stable, machine-readable failure codes. The server returns them in API errors and the client
    /// maps them to localized messages, so never rename an existing value.
    /// </summary>
    public enum ErrorCode
    {
        None = 0,
        InvalidArgument,
        NotFound,
        NotAdjacent,
        NotSwappable,
        NoMatch,
        SessionOver,
        TooFast,
        TimestampOutOfOrder,
        TimeExpired,
        PowerUpLocked,
        PowerUpNotInLoadout,
        PowerUpAlreadyUsed,
        PowerUpNotAvailableInMode,
        PowerUpNeedsTarget,
        NotEnoughCoins,
        NotEnoughOrbes,
        NotEnoughLives,
        NotEnoughItems,
        LimitReached,
        CooldownActive,
        FeatureLocked,
        StageLocked,
        AlreadyClaimed,
        AlreadyMember,
        NotMember,
        GuildFull,
        PermissionDenied,
        NameInvalid,
        NameTaken,
        Banned,
        ReplayInvalid,
        ReplayMismatch,
        VersionMismatch,
        DuplicateRequest,
        MessageRejected
    }

    /// <summary>Result of an operation that can fail for gameplay/business reasons (not exceptions).</summary>
    public readonly struct OperationResult
    {
        public readonly ErrorCode Error;
        public readonly string Message;

        private OperationResult(ErrorCode error, string message)
        {
            Error = error;
            Message = message;
        }

        public bool Success => Error == ErrorCode.None;

        public static OperationResult Ok() => new OperationResult(ErrorCode.None, null);

        public static OperationResult Fail(ErrorCode error, string message = null) => new OperationResult(error, message ?? error.ToString());

        public override string ToString() => Success ? "OK" : Error + ": " + Message;
    }

    /// <summary>Result carrying a value on success.</summary>
    public readonly struct OperationResult<T>
    {
        public readonly ErrorCode Error;
        public readonly string Message;
        public readonly T Value;

        private OperationResult(ErrorCode error, string message, T value)
        {
            Error = error;
            Message = message;
            Value = value;
        }

        public bool Success => Error == ErrorCode.None;

        public static OperationResult<T> Ok(T value) => new OperationResult<T>(ErrorCode.None, null, value);

        public static OperationResult<T> Fail(ErrorCode error, string message = null) =>
            new OperationResult<T>(error, message ?? error.ToString(), default);

        public OperationResult WithoutValue() => Success ? OperationResult.Ok() : OperationResult.Fail(Error, Message);

        public override string ToString() => Success ? "OK(" + Value + ")" : Error + ": " + Message;
    }
}
