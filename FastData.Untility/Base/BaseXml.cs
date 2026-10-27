using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

#if NETFRAMEWORK
using System.Web;
#endif

namespace FastUntility.Base
{
    /// <summary>
    /// XML 处理类
    /// 提供 XML 解析、序列化和反序列化功能
    /// </summary>
    public static class BaseXml
    {
        /// <summary>
        /// 从 XML 字符串中提取指定节点的对象列表
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="xmlValue">完整的 XML 字符串</param>
        /// <param name="xpath">节点 XPath 表达式</param>
        /// <returns>反序列化后的对象列表，解析失败时返回空列表</returns>
        public static List<T> GetXmlList<T>(string xmlValue, string xpath)
        {
            if (string.IsNullOrEmpty(xmlValue) || string.IsNullOrEmpty(xpath))
                return new List<T>();

            try
            {
                var xmlHead = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";
                var xmlDoc = new XmlDocument();
                var list = new List<T>();

                xmlDoc.LoadXml(xmlValue);
                var nodeList = xmlDoc.SelectNodes(xpath);

                if (nodeList == null)
                    return list;

                foreach (XmlNode node in nodeList)
                {
                    var nodeXml = string.Format("{0}<{1}>{2}</{1}>", xmlHead, node.LocalName, node.InnerXml);

                    using (var reader = new StringReader(nodeXml))
                    {
                        var serializer = new XmlSerializer(typeof(T));
                        list.Add((T)serializer.Deserialize(reader));
                    }
                }

                return list;
            }
            catch (Exception ex)
            {
                // 数据错误：XML 解析失败却返回空列表，调用方会当成“确实没有数据”，
                // 是最典型的静默数据错误。签名不能改，必须用日志让失败可见。
                BaseLog.SaveLog(string.Format("GetXmlList<{0}> 解析失败，xpath：{1}，异常：{2}",
                    typeof(T).FullName, xpath, ex), "BaseXml_exp");
                return new List<T>();
            }
        }

        /// <summary>
        /// 从 XML 字符串中提取指定节点的文本列表
        /// </summary>
        /// <param name="xmlValue">完整的 XML 字符串</param>
        /// <param name="xpath">节点 XPath 表达式</param>
        /// <returns>节点文本列表，解析失败时返回空列表</returns>
        public static List<string> GetXmlList(string xmlValue, string xpath)
        {
            if (string.IsNullOrEmpty(xmlValue) || string.IsNullOrEmpty(xpath))
                return new List<string>();

            try
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xmlValue);
                var nodeList = xmlDoc.SelectNodes(xpath);

                var list = new List<string>();

                if (nodeList == null)
                    return list;

                foreach (XmlNode node in nodeList)
                {
                    list.Add(node.InnerXml);
                }

                return list;
            }
            catch (Exception ex)
            {
                // 数据错误：XML 解析失败返回空列表与“无匹配节点”无法区分，
                // 必须用日志让失败可见。
                BaseLog.SaveLog(string.Format("GetXmlList 解析失败，xpath：{0}，异常：{1}",
                    xpath, ex), "BaseXml_exp");
                return new List<string>();
            }
        }

        /// <summary>
        /// 从 XML 字符串中提取第一个匹配节点的文本
        /// </summary>
        /// <param name="xmlValue">完整的 XML 字符串</param>
        /// <param name="xpath">节点 XPath 表达式</param>
        /// <returns>第一个节点的文本，不存在时返回空字符串</returns>
        public static string GetXmlString(string xmlValue, string xpath)
        {
            var list = GetXmlList(xmlValue, xpath);
            return list.Count > 0 ? list[0] : string.Empty;
        }

#if NETFRAMEWORK
        /// <summary>
        /// 从 XML 文件中提取指定节点的文本列表（支持 HttpContextBase）
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <param name="fileName">XML 文件虚拟路径</param>
        /// <param name="xpath">节点 XPath 表达式</param>
        /// <returns>节点文本列表，解析失败时返回空列表</returns>
        public static List<string> GetXmlListFromFileAsync(HttpContextBase context, string fileName, string xpath)
        {
            if (context == null || string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(xpath))
                return new List<string>();

            try
            {
                var xmlDoc = new XmlDocument();
                var filePath = context.Server.MapPath(fileName);
                xmlDoc.Load(filePath);
                var nodeList = xmlDoc.SelectNodes(xpath);

                var list = new List<string>();

                if (nodeList == null)
                    return list;

                foreach (XmlNode node in nodeList)
                {
                    list.Add(node.InnerXml);
                }

                return list;
            }
            catch (Exception ex)
            {
                // 数据错误：XML 文件解析失败返回空列表与“无匹配节点”无法区分，
                // 必须用日志让失败可见。
                BaseLog.SaveLog(string.Format("GetXmlListFromFileAsync 解析失败，文件：{0}，xpath：{1}，异常：{2}",
                    fileName, xpath, ex), "BaseXml_exp");
                return new List<string>();
            }
        }

        /// <summary>
        /// 从 XML 文件中提取指定节点的文本列表
        /// </summary>
        /// <param name="fileName">XML 文件虚拟路径</param>
        /// <param name="xpath">节点 XPath 表达式</param>
        /// <returns>节点文本列表，解析失败时返回空列表</returns>
        public static List<string> GetXmlListFromFile(string fileName, string xpath)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(xpath))
                return new List<string>();

            try
            {
                var current = HttpContext.Current;
                if (current == null)
                    return new List<string>();

                var xmlDoc = new XmlDocument();
                var filePath = current.Server.MapPath(fileName);
                xmlDoc.Load(filePath);
                var nodeList = xmlDoc.SelectNodes(xpath);

                var list = new List<string>();

                if (nodeList == null)
                    return list;

                foreach (XmlNode node in nodeList)
                {
                    list.Add(node.InnerXml);
                }

                return list;
            }
            catch (Exception ex)
            {
                // 数据错误：XML 文件解析失败返回空列表与“无匹配节点”无法区分，
                // 必须用日志让失败可见。
                BaseLog.SaveLog(string.Format("GetXmlListFromFile 解析失败，文件：{0}，xpath：{1}，异常：{2}",
                    fileName, xpath, ex), "BaseXml_exp");
                return new List<string>();
            }
        }

        /// <summary>
        /// 从 XML 文件中提取第一个匹配节点的文本
        /// </summary>
        /// <param name="fileName">XML 文件虚拟路径</param>
        /// <param name="xpath">节点 XPath 表达式</param>
        /// <returns>第一个节点的文本，不存在时返回空字符串</returns>
        public static string GetXmlStringFromFile(string fileName, string xpath)
        {
            var list = GetXmlListFromFile(fileName, xpath);
            return list.Count > 0 ? list[0] : string.Empty;
        }
#endif
    }
}
