using System;
using System.Collections;
using System.ServiceProcess;
using Microsoft.Win32;
using System.Configuration.Install;
using FastUntility.Base;

namespace FastUntility.WinService
{
    /// <summary>
    /// windows 服务
    /// </summary>
    public static class Service
    {
        #region 服务重起
        /// <summary>
        /// 服务重起
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <param name="IsForced">是否强制重起服务</param>
        public static bool ReStatService(string serviceName, bool IsForced = false)
        {
            var returnValue = true;
            try
            {
                var mySevice = new ServiceController(serviceName);

                if (IsForced)
                {
                    mySevice.Stop();
                    mySevice.WaitForStatus(ServiceControllerStatus.Stopped);
                    mySevice.Start();
                    mySevice.WaitForStatus(ServiceControllerStatus.Running);
                }
                else
                {
                    if (mySevice.Status != ServiceControllerStatus.Running
                        && mySevice.Status != ServiceControllerStatus.StartPending)
                    {
                        mySevice.Start();
                        mySevice.WaitForStatus(ServiceControllerStatus.Running);
                    }
                }
            }
            catch (Exception ex)
            {
                // 降级合理：服务控制失败时返回 false（调用方已有可判定信号），
                // 但必须记日志，否则“重起失败”永远查不到原因。
                BaseLog.SaveLog(string.Format("ReStatService 重起服务 {0} 失败：{1}", serviceName, ex), "WinService_exp");
                return false;
            }

            return returnValue;
        }
        #endregion

        #region 返回服务状态
        /// <summary>
        /// 返回服务状态
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        public static ServiceControllerStatus GetServiceStatus(string serviceName)
        {
            try
            {
                var mySevice = new ServiceController(serviceName);

                return mySevice.Status;
            }
            catch (Exception ex)
            {
                // 降级合理：取不到状态时返回 ContinuePending，但该值不是真实状态，
                // 必须记日志避免调用方误判服务状态。
                BaseLog.SaveLog(string.Format("GetServiceStatus 获取服务 {0} 状态失败：{1}", serviceName, ex), "WinService_exp");
                return ServiceControllerStatus.ContinuePending;
            }
        }
        #endregion

        #region 服务停止
        /// <summary>
        /// 服务停止
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        public static bool StopService(string serviceName)
        {
            var returnValue = true;
            try
            {
                var mySevice = new ServiceController(serviceName);

                if (mySevice.Status == ServiceControllerStatus.Stopped
                    && mySevice.Status == ServiceControllerStatus.StopPending)
                    returnValue = true;
            }
            catch (Exception ex)
            {
                // 降级合理：停服务失败返回 false（可判定），但需记录失败原因。
                BaseLog.SaveLog(string.Format("StopService 停止服务 {0} 失败：{1}", serviceName, ex), "WinService_exp");
                returnValue = false;
            }

            return returnValue;
        }
        #endregion

        #region 修改服务的启动项
        /// <summary>    
        /// 修改服务的启动项    
        /// </summary>    
        /// <param name="startType">2为自动,3为手动 </param>    
        /// <param name="serviceName">服务名称</param>    
        /// <returns></returns>    
        public static bool ChangeServiceStartType(int startType, string serviceName)
        {
            try
            {
                var regist = Registry.LocalMachine;
                var sysReg = regist.OpenSubKey("SYSTEM");
                var currentControlSet = sysReg.OpenSubKey("CurrentControlSet");
                var services = currentControlSet.OpenSubKey("Services");
                var servicesName = services.OpenSubKey(serviceName, true);
                servicesName.SetValue("Start", startType);
            }
            catch (Exception ex)
            {
                // 降级合理：注册表写入失败返回 false（可判定），但必须记录原因，
                // 否则“改了启动类型没生效”无法排查（常见于权限不足）。
                BaseLog.SaveLog(string.Format("ChangeServiceStartType 修改服务 {0} 启动类型为 {1} 失败：{2}", serviceName, startType, ex), "WinService_exp");
                return false;
            }

            return true;
        }
        #endregion

        #region 安装服务
        /// <summary>  
        /// 安装服务  
        /// </summary>  
        public static bool InstallService(string serviceName)
        {
            var flag = true;
            if (!IsExistsService(serviceName))
            {
                try
                {
                    var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var serviceFileName = location.Substring(0, location.LastIndexOf('\\') + 1) + serviceName + ".exe";
                    InstallmyService(null, serviceFileName);
                }
                catch (Exception ex)
                {
                    // 降级合理：安装失败返回 false（可判定），但必须记录原因。
                    BaseLog.SaveLog(string.Format("InstallService 安装服务 {0} 失败：{1}", serviceName, ex), "WinService_exp");
                    flag = false;
                }

            }
            return flag;
        }
        #endregion 

        #region 卸载服务
        /// <summary>  
        /// 卸载服务  
        /// </summary>  
        public static bool UninstallService(string serviceName)
        {
            var flag = true;
            if (IsExistsService(serviceName))
            {
                try
                {
                    var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var serviceFileName = location.Substring(0, location.LastIndexOf('\\') + 1) + serviceName + ".exe";
                    UnInstallmyService(serviceFileName);
                }
                catch (Exception ex)
                {
                    // 降级合理：卸载失败返回 false（可判定），但必须记录原因。
                    BaseLog.SaveLog(string.Format("UninstallService 卸载服务 {0} 失败：{1}", serviceName, ex), "WinService_exp");
                    flag = false;
                }
            }
            return flag;
        }
        #endregion  

        #region 检查服务是否存在基类
        /// <summary>  
        /// 检查服务是否存在基类
        /// </summary>  
        /// <param name="serviceName">服务名</param>  
        /// <returns></returns>  
        private static bool IsExistsService(string serviceName)
        {
            var services = ServiceController.GetServices();
            foreach (ServiceController s in services)
            {
                if (s.ServiceName.ToLower() == serviceName.ToLower())
                {
                    return true;
                }
            }
            return false;
        }
        #endregion

        #region 卸载Windows服务基类
        /// <summary>  
        /// 卸载Windows服务  
        /// </summary>  
        /// <param name="filepath">程序文件路径</param>  
        private static void UnInstallmyService(string filepath)
        {
            var AssemblyInstaller1 = new AssemblyInstaller();
            AssemblyInstaller1.UseNewContext = true;
            AssemblyInstaller1.Path = filepath;
            AssemblyInstaller1.Uninstall(null);
            AssemblyInstaller1.Dispose();
        }
        #endregion  

        #region 安装Windows服务基类
        /// <summary>  
        /// 安装Windows服务  
        /// </summary>  
        /// <param name="stateSaver">集合</param>  
        /// <param name="filepath">程序文件路径</param>  
        private static void InstallmyService(IDictionary stateSaver, string filepath)
        {
            var AssemblyInstaller1 = new AssemblyInstaller();
            AssemblyInstaller1.UseNewContext = true;
            AssemblyInstaller1.Path = filepath;
            AssemblyInstaller1.Install(stateSaver);
            AssemblyInstaller1.Commit(stateSaver);
            AssemblyInstaller1.Dispose();
        }
        #endregion  
    }
}
