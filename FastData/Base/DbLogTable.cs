using FastData.Model;
using System;
using FastData.Context;
using FastData.DbTypes;

namespace FastData.Base
{
    internal static class DbLogTable
    {
        private static bool _isSaving = false;

        /// <summary>
        /// 数据库出错日志
        /// </summary>
        /// <param name="config">配置模型</param>
        /// <param name="ex">异常对象</param>
        /// <param name="CurrentMethod">当前方法名</param>
        /// <param name="sql">SQL语句</param>
        public static void LogException<T>(ConfigModel config, Exception ex, string CurrentMethod, string sql)
        {
            SaveToDb(config, ex, CurrentMethod, sql, typeof(T).Name);
        }


        /// <summary>
        /// 数据库出错日志
        /// </summary>
        /// <param name="config">配置模型</param>
        /// <param name="ex">异常对象</param>
        /// <param name="CurrentMethod">当前方法名</param>
        /// <param name="sql">SQL语句</param>
        public static void LogException(ConfigModel config, Exception ex, string CurrentMethod, string sql)
        {
            // 记日志本身绝不能抛异常：它是在处理「已经出错」的场景下被调用的。
            // 之前的实现在这里再 new DataContext，连接池耗尽或库不可达时会再抛一次，
            // 把原始异常顶掉（堆栈表现为 DataContext → LogException → DataContext 的递归），
            // 调用方看到的错误和真正的故障原因对不上。
            try
            {
                SaveToDb(config, ex, CurrentMethod, sql, "");
            }
            catch (Exception logEx)
            {
                System.Diagnostics.Trace.TraceError(
                    string.Format("记录数据库错误日志失败（原始错误：{0}: {1}）：{2}",
                        ex.GetType().Name, ex.Message, logEx));
            }
        }

        /// <summary>
        /// 存数据库
        /// </summary>
        /// <param name="config">配置模型</param>
        /// <param name="ex">异常对象</param>
        /// <param name="CurrentMethod">当前方法名</param>
        /// <param name="sql">SQL语句</param>
        /// <param name="type">类型名称</param>
        private static void SaveToDb(ConfigModel config, Exception ex, string CurrentMethod, string sql,string type)
        {
            if (config.IsOutError && !_isSaving)
            {
                _isSaving = true;
                try
                {
                    using (var db = new DataContext(config.Key))
                    {
                        if (config.DbType == DataDbType.MySql)
                        {
                            var model = new FastData.DataModel.MySql.Data_LogError();
                            model.AddTime = DateTime.Now;
                            model.Content = ex.StackTrace;
                            model.ErrorId = Guid.NewGuid().ToString();
                            model.Method = CurrentMethod;
                            model.Type = type;
                            model.Sql = sql;
                            db.Add(model);
                        }

                        if (config.DbType == DataDbType.Oracle)
                        {
                            var model = new FastData.DataModel.Oracle.Data_LogError();
                            model.AddTime = DateTime.Now;
                            model.Content = ex.StackTrace;
                            model.ErrorId = Guid.NewGuid().ToString();
                            model.Method = CurrentMethod;
                            model.Type = type;
                            model.Sql = sql;
                            db.Add(model);
                        }

                        if (config.DbType == DataDbType.SqlServer)
                        {
                            var model = new FastData.DataModel.SqlServer.Data_LogError();
                            model.AddTime = DateTime.Now;
                            model.Content = ex.StackTrace;
                            model.ErrorId = Guid.NewGuid().ToString();
                            model.Method = CurrentMethod;
                            model.Type = type;
                            model.Sql = sql;
                            db.Add(model);
                        }
                    }
                }
                finally
                {
                    _isSaving = false;
                }
            }
        }
    }
}
