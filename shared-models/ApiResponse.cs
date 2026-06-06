namespace shared_models
{
    /// <summary>
    /// Standardized API response wrapper for all REST endpoints
    /// </summary>
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public ApiError? Error { get; set; }

        public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };
        public static ApiResponse<T> Fail(string code, string message, int statusCode = 400) =>
            new() { Success = false, Error = new ApiError { Code = code, Message = message, StatusCode = statusCode } };
    }

    public class ApiError
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int StatusCode { get; set; }
    }
}
