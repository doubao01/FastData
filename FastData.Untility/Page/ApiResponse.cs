#nullable enable
using System;
using FastUntility.Base;

namespace FastUntility.Page
{
    /// <summary>
    /// 统一 API 响应格式
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class ApiResponse<T>
    {
        /// <summary>
        /// 状态码（0=成功，其他=失败）
        /// </summary>
        public int Code { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 数据
        /// </summary>
        public T? Data { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public long Timestamp { get; set; } = FrameworkCompat.ToUnixTimeMilliseconds(DateTimeOffset.UtcNow);

        /// <summary>
        /// 请求 ID
        /// </summary>
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success => Code == 0;

        #region 静态工厂方法
        /// <summary>
        /// 成功响应
        /// </summary>
        public static ApiResponse<T> Ok(T data, string message = "操作成功")
        {
            return new ApiResponse<T>
            {
                Code = 0,
                Message = message,
                Data = data
            };
        }

        /// <summary>
        /// 失败响应
        /// </summary>
        public static ApiResponse<T> Fail(string message, int code = -1)
        {
            return new ApiResponse<T>
            {
                Code = code,
                Message = message
            };
        }

        /// <summary>
        /// 失败响应（带数据）
        /// </summary>
        public static ApiResponse<T> Fail(string message, T data, int code = -1)
        {
            return new ApiResponse<T>
            {
                Code = code,
                Message = message,
                Data = data
            };
        }

        /// <summary>
        /// 未找到
        /// </summary>
        public static ApiResponse<T> NotFound(string message = "数据不存在")
        {
            return new ApiResponse<T>
            {
                Code = 404,
                Message = message
            };
        }

        /// <summary>
        /// 未授权
        /// </summary>
        public static ApiResponse<T> Unauthorized(string message = "未授权")
        {
            return new ApiResponse<T>
            {
                Code = 401,
                Message = message
            };
        }

        /// <summary>
        /// 禁止访问
        /// </summary>
        public static ApiResponse<T> Forbidden(string message = "禁止访问")
        {
            return new ApiResponse<T>
            {
                Code = 403,
                Message = message
            };
        }
        #endregion
    }

    /// <summary>
    /// 统一 API 响应格式（无数据）
    /// </summary>
    public class ApiResponse : ApiResponse<object>
    {
        /// <summary>
        /// 成功响应（无数据）
        /// </summary>
        public static ApiResponse Ok(string message = "操作成功")
        {
            return new ApiResponse
            {
                Code = 0,
                Message = message
            };
        }

        /// <summary>
        /// 失败响应
        /// </summary>
        public new static ApiResponse Fail(string message, int code = -1)
        {
            return new ApiResponse
            {
                Code = code,
                Message = message
            };
        }
    }
}
