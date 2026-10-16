using System;
using FastData.Context;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FastData.DevTools
{
    /// <summary>
    /// 事件总线
    /// </summary>
    public static class EventBus
    {
        private static readonly ConcurrentDictionary<Type, List<EventHandlerInfo>> _handlers = new ConcurrentDictionary<Type, List<EventHandlerInfo>>();
        private static readonly ConcurrentQueue<EventMessage> _eventQueue = new ConcurrentQueue<EventMessage>();
        private static readonly object _lock = new object();
        private static bool _isProcessing;
        private static Task _processingTask;

        /// <summary>
        /// 订阅事件
        /// </summary>
        public static IDisposable Subscribe<TEvent>(Action<TEvent> handler, string subscriberId = null) where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            var handlerInfo = new EventHandlerInfo
            {
                Handler = @event => handler((TEvent)@event),
                SubscriberId = subscriberId ?? Guid.NewGuid().ToString(),
                EventType = eventType
            };

            lock (_lock)
            {
                if (!_handlers.ContainsKey(eventType))
                {
                    _handlers[eventType] = new List<EventHandlerInfo>();
                }
                _handlers[eventType].Add(handlerInfo);
            }

            return new Subscription(() => Unsubscribe(eventType, handlerInfo.SubscriberId));
        }

        /// <summary>
        /// 取消订阅
        /// </summary>
        public static void Unsubscribe<TEvent>(string subscriberId) where TEvent : IEvent
        {
            Unsubscribe(typeof(TEvent), subscriberId);
        }

        /// <summary>
        /// 发布事件（同步）
        /// </summary>
        public static void Publish<TEvent>(TEvent @event) where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            var handlers = GetHandlers(eventType);

            foreach (var handler in handlers)
            {
                try
                {
                    handler.Handler(@event);
                }
                catch (Exception ex)
                {
                    // 记录错误，但不影响其他处理器
                    LogAggregator.Exception(ex, string.Format("事件处理器执行失败: {0}", eventType.Name), "EventBus");
                }
            }
        }

        /// <summary>
        /// 发布事件（异步）
        /// </summary>
        public static Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent
        {
            return Task.Run(() => Publish(@event));
        }

        /// <summary>
        /// 发布事件（队列模式）
        /// </summary>
        public static void Enqueue<TEvent>(TEvent @event) where TEvent : IEvent
        {
            var message = new EventMessage
            {
                Event = @event,
                EventType = typeof(TEvent),
                PublishedAt = DateTime.Now
            };

            _eventQueue.Enqueue(message);
            StartProcessing();
        }

        /// <summary>
        /// 等待事件队列处理完成
        /// </summary>
        public static async Task WaitUntilQueueEmptyAsync()
        {
            while (!_eventQueue.IsEmpty || _isProcessing)
            {
                await Task.Delay(100);
            }
        }

        /// <summary>
        /// 获取订阅者数量
        /// </summary>
        public static int GetSubscriberCount<TEvent>() where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            lock (_lock)
            {
                return _handlers.TryGetValue(eventType, out var handlers) ? handlers.Count : 0;
            }
        }

        /// <summary>
        /// 获取所有事件类型
        /// </summary>
        public static List<Type> GetAllEventTypes()
        {
            lock (_lock)
            {
                return _handlers.Keys.ToList();
            }
        }

        /// <summary>
        /// 清除所有订阅
        /// </summary>
        public static void ClearAllSubscriptions()
        {
            lock (_lock)
            {
                _handlers.Clear();
            }
        }

        private static void Unsubscribe(Type eventType, string subscriberId)
        {
            lock (_lock)
            {
                if (_handlers.TryGetValue(eventType, out var handlers))
                {
                    var handler = handlers.FirstOrDefault(h => h.SubscriberId == subscriberId);
                    if (handler != null)
                    {
                        handlers.Remove(handler);
                    }
                }
            }
        }

        private static List<EventHandlerInfo> GetHandlers(Type eventType)
        {
            lock (_lock)
            {
                if (_handlers.TryGetValue(eventType, out var handlers))
                {
                    return handlers.ToList();
                }
            }
            return new List<EventHandlerInfo>();
        }

        private static void StartProcessing()
        {
            lock (_lock)
            {
                if (_isProcessing) return;
                _isProcessing = true;
                _processingTask = ProcessQueueAsync();
            }
        }

        private static async Task ProcessQueueAsync()
        {
            while (_eventQueue.TryDequeue(out var message))
            {
                try
                {
                    var handlers = GetHandlers(message.EventType);
                    var tasks = handlers.Select(handler => Task.Run(() =>
                    {
                        try
                        {
                            handler.Handler(message.Event);
                        }
                        catch (Exception ex)
                        {
                            LogAggregator.Exception(ex, string.Format("事件处理器执行失败: {0}", message.EventType.Name), "EventBus");
                        }
                    }));

                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    LogAggregator.Exception(ex, string.Format("事件处理失败: {0}", message.EventType.Name), "EventBus");
                }
            }

            lock (_lock)
            {
                if (_eventQueue.IsEmpty)
                {
                    _isProcessing = false;
                }
                else
                {
                    _processingTask = ProcessQueueAsync();
                }
            }
        }
    }

    /// <summary>
    /// 事件接口
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// 事件唯一标识
        /// </summary>
        string EventId { get; set; }
        /// <summary>
        /// 事件发生时间
        /// </summary>
        DateTime OccurredAt { get; set; }
    }

    /// <summary>
    /// 事件基础类
    /// </summary>
    public abstract class EventBase : IEvent
    {
        /// <summary>
        /// 事件唯一标识（默认自动生成）
        /// </summary>
        public string EventId { get; set; } = Guid.NewGuid().ToString();
        /// <summary>
        /// 事件发生时间（默认当前时间）
        /// </summary>
        public DateTime OccurredAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 事件处理器信息
    /// </summary>
    internal class EventHandlerInfo
    {
        public Type EventType { get; set; }
        public string SubscriberId { get; set; }
        public Action<IEvent> Handler { get; set; }
    }

    /// <summary>
    /// 事件消息
    /// </summary>
    internal class EventMessage
    {
        public IEvent Event { get; set; }
        public Type EventType { get; set; }
        public DateTime PublishedAt { get; set; }
    }

    /// <summary>
    /// 订阅
    /// </summary>
    public class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;

        /// <summary>
        /// 创建订阅对象
        /// </summary>
        /// <param name="unsubscribe">取消订阅的回调</param>
        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        /// <summary>
        /// 释放订阅并执行取消订阅回调
        /// </summary>
        public void Dispose()
        {
            _unsubscribe?.Invoke();
        }
    }

    /// <summary>
    /// 域事件总线
    /// </summary>
    public static class DomainEventBus
    {
        private static readonly ConcurrentQueue<IEvent> _events = new ConcurrentQueue<IEvent>();
        private static readonly object _lock = new object();
        private static bool _isProcessing;

        /// <summary>
        /// 发布域事件
        /// </summary>
        public static void Publish(IEvent @event)
        {
            _events.Enqueue(@event);
            StartProcessing();
        }

        /// <summary>
        /// 发布域事件集合
        /// </summary>
        public static void PublishRange(IEnumerable<IEvent> events)
        {
            foreach (var @event in events)
            {
                _events.Enqueue(@event);
            }
            StartProcessing();
        }

        /// <summary>
        /// 处理所有未处理的事件
        /// </summary>
        public static void ProcessPendingEvents()
        {
            while (_events.TryDequeue(out var @event))
            {
                try
                {
                    EventBus.Publish(@event);
                }
                catch (Exception ex)
                {
                    LogAggregator.Exception(ex, string.Format("域事件处理失败: {0}", @event.EventId), "DomainEventBus");
                }
            }

            lock (_lock)
            {
                _isProcessing = false;
            }
        }

        /// <summary>
        /// 异步处理所有未处理的事件
        /// </summary>
        public static async Task ProcessPendingEventsAsync()
        {
            await Task.Run(() => ProcessPendingEvents());
        }

        /// <summary>
        /// 检查是否有待处理的事件
        /// </summary>
        public static bool HasPendingEvents()
        {
            return !_events.IsEmpty;
        }

        /// <summary>
        /// 获取待处理事件数量
        /// </summary>
        public static int GetPendingEventCount()
        {
            return _events.Count;
        }

        private static void StartProcessing()
        {
            lock (_lock)
            {
                if (_isProcessing) return;
                _isProcessing = true;
                Task.Run(() => ProcessPendingEvents());
            }
        }
    }

    /// <summary>
    /// 事件溯源
    /// </summary>
    public static class EventSourcing
    {
        private static readonly List<IEvent> _eventStore = new List<IEvent>();
        private static readonly object _lock = new object();

        /// <summary>
        /// 保存事件
        /// </summary>
        public static void SaveEvent(IEvent @event)
        {
            lock (_lock)
            {
                _eventStore.Add(@event);
            }
        }

        /// <summary>
        /// 获取事件流
        /// </summary>
        public static List<IEvent> GetEventStream(DateTime? fromTime = null, DateTime? toTime = null)
        {
            lock (_lock)
            {
                var events = _eventStore.AsEnumerable();

                if (fromTime.HasValue)
                {
                    events = events.Where(e => e.OccurredAt >= fromTime.Value);
                }

                if (toTime.HasValue)
                {
                    events = events.Where(e => e.OccurredAt <= toTime.Value);
                }

                return events.ToList();
            }
        }

        /// <summary>
        /// 重放事件
        /// </summary>
        public static void ReplayEvents(Action<IEvent> handler, DateTime? fromTime = null, DateTime? toTime = null)
        {
            var events = GetEventStream(fromTime, toTime);
            foreach (var @event in events)
            {
                try
                {
                    handler(@event);
                }
                catch (Exception ex)
                {
                    LogAggregator.Exception(ex, string.Format("事件重放失败: {0}", @event.EventId), "EventSourcing");
                }
            }
        }

        /// <summary>
        /// 异步重放事件
        /// </summary>
        public static async Task ReplayEventsAsync(Func<IEvent, Task> handler, DateTime? fromTime = null, DateTime? toTime = null)
        {
            var events = GetEventStream(fromTime, toTime);
            foreach (var @event in events)
            {
                try
                {
                    await handler(@event);
                }
                catch (Exception ex)
                {
                    LogAggregator.Exception(ex, string.Format("事件重放失败: {0}", @event.EventId), "EventSourcing");
                }
            }
        }

        /// <summary>
        /// 清除事件存储
        /// </summary>
        public static void ClearEventStore()
        {
            lock (_lock)
            {
                _eventStore.Clear();
            }
        }

        /// <summary>
        /// 获取事件数量
        /// </summary>
        public static int GetEventCount()
        {
            lock (_lock)
            {
                return _eventStore.Count;
            }
        }
    }

    /// <summary>
    /// 常见事件定义
    /// </summary>
    public class CommonEvents
    {
        /// <summary>
        /// 实体创建事件
        /// </summary>
        public class EntityCreatedEvent : EventBase
        {
            /// <summary>
            /// 实体类型名称
            /// </summary>
            public string EntityType { get; set; }
            /// <summary>
            /// 实体标识
            /// </summary>
            public string EntityId { get; set; }
            /// <summary>
            /// 实体数据
            /// </summary>
            public object Data { get; set; }
        }

        /// <summary>
        /// 实体更新事件
        /// </summary>
        public class EntityUpdatedEvent : EventBase
        {
            /// <summary>
            /// 实体类型名称
            /// </summary>
            public string EntityType { get; set; }
            /// <summary>
            /// 实体标识
            /// </summary>
            public string EntityId { get; set; }
            object OldData { get; set; }
            object NewData { get; set; }
        }

        /// <summary>
        /// 实体删除事件
        /// </summary>
        public class EntityDeletedEvent : EventBase
        {
            /// <summary>
            /// 实体类型名称
            /// </summary>
            public string EntityType { get; set; }
            /// <summary>
            /// 实体标识
            /// </summary>
            public string EntityId { get; set; }
        }

        /// <summary>
        /// 错误事件
        /// </summary>
        public class ErrorEvent : EventBase
        {
            /// <summary>
            /// 错误消息
            /// </summary>
            public string ErrorMessage { get; set; }
            /// <summary>
            /// 异常类型名称
            /// </summary>
            public string ExceptionType { get; set; }
            /// <summary>
            /// 异常堆栈信息
            /// </summary>
            public string StackTrace { get; set; }
        }
    }
}