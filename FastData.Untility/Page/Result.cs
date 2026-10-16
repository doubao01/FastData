#nullable enable
using System;

namespace FastUntility.Page
{
    /// <summary>
    /// 统一结果类型
    /// </summary>
    public class Result
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 错误码
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// 异常信息
        /// </summary>
        public Exception? Exception { get; set; }

        #region 静态工厂方法
        /// <summary>
        /// 成功
        /// </summary>
        public static Result Ok(string message = "操作成功")
        {
            return new Result { IsSuccess = true, Message = message };
        }

        /// <summary>
        /// 失败
        /// </summary>
        public static Result Fail(string message, string? errorCode = null)
        {
            return new Result { IsSuccess = false, Message = message, ErrorCode = errorCode };
        }

        /// <summary>
        /// 失败（带异常）
        /// </summary>
        public static Result Fail(Exception ex, string? errorCode = null)
        {
            return new Result
            {
                IsSuccess = false,
                Message = ex.Message,
                ErrorCode = errorCode,
                Exception = ex
            };
        }
        #endregion

        #region 转换
        /// <summary>
        /// 转换为带数据的结果
        /// </summary>
        public Result<T> ToResult<T>(T data)
        {
            return new Result<T>
            {
                IsSuccess = IsSuccess,
                Message = Message,
                ErrorCode = ErrorCode,
                Exception = Exception,
                Data = data
            };
        }
        #endregion
    }

    /// <summary>
    /// 统一结果类型（带数据）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class Result<T> : Result
    {
        /// <summary>
        /// 数据
        /// </summary>
        public T? Data { get; set; }

        #region 静态工厂方法
        /// <summary>
        /// 成功（带数据）
        /// </summary>
        public static Result<T> Ok(T data, string message = "操作成功")
        {
            return new Result<T>
            {
                IsSuccess = true,
                Message = message,
                Data = data
            };
        }

        /// <summary>
        /// 失败
        /// </summary>
        public new static Result<T> Fail(string message, string? errorCode = null)
        {
            return new Result<T>
            {
                IsSuccess = false,
                Message = message,
                ErrorCode = errorCode
            };
        }

        /// <summary>
        /// 失败（带异常）
        /// </summary>
        public new static Result<T> Fail(Exception ex, string? errorCode = null)
        {
            return new Result<T>
            {
                IsSuccess = false,
                Message = ex.Message,
                ErrorCode = errorCode,
                Exception = ex
            };
        }
        #endregion

        #region 转换
        /// <summary>
        /// 转换为 ApiResponse
        /// </summary>
        public ApiResponse<T> ToApiResponse()
        {
            if (IsSuccess)
                return ApiResponse<T>.Ok(Data ?? default!, Message);
            else
                return ApiResponse<T>.Fail(Message, ErrorCode != null ? int.TryParse(ErrorCode, out var code) ? code : -1 : -1);
        }

        /// <summary>
        /// 转换为无数据的 Result
        /// </summary>
        public Result ToResult()
        {
            return new Result
            {
                IsSuccess = IsSuccess,
                Message = Message,
                ErrorCode = ErrorCode,
                Exception = Exception
            };
        }
        #endregion
    }
}
