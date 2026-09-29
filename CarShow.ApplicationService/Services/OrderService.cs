using Carshow.Utilities.Convertor;
using CarShow.ApplicationService.Contract.Dtos.Order;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace CarShow.ApplicationService.Services
{
    public class OrderService : IOrderService
    {
        private readonly IBaseRepository<long,Order> _orderRepository;
        private readonly IBaseRepository<Guid, User> _userRepository;
        private readonly IBaseRepository<long, Car> _carRepository;
        private readonly IBaseRepository<long, OrderItem> _orderItemRepository;
        private readonly IBaseRepository<long,UserRole> _userRoleRepository;
        private readonly IInstallmentCalculator _installmentCalculator;
        private readonly IUnitOfWork unitOfWork;
        public OrderService(
            IBaseRepository<long, Order> orderRepository,
            IBaseRepository<Guid, User> userRepository,
            IBaseRepository<long, Car> carRepository,
            IBaseRepository<long, UserRole> roleRepository,
            IUnitOfWork unitOfWork,
            IInstallmentCalculator installmentCalculator,
            IBaseRepository<long, OrderItem> orderItemRepository)
        {
            _orderRepository = orderRepository;
            _userRepository = userRepository;
            _carRepository = carRepository;
            _userRoleRepository = roleRepository;
            this.unitOfWork = unitOfWork;
            _installmentCalculator = installmentCalculator;
            _orderItemRepository = orderItemRepository;
        }

        public async Task<Result<List<OrderDto>>> GetOrders()
        {
            var orders = await _orderRepository.GetAll()
                .Select(x => new OrderDto
                {
                    Id = x.Id,
                    CarName = x.Car != null ? x.Car.Name : "خودرو نامشخص",
                    RoleName = x.Role != null ? x.Role.RoleName : "نقش نامشخص",
                    UserName = x.User != null
                        ? ((x.User.Name ?? "") + " " + (x.User.Family ?? ""))
                        : "کاربر نامشخص",
                    CreateOrderDate = x.CreateOrderDate
                })
                .ToListAsync();

            return Result<List<OrderDto>>.Success(orders);
        }
        public async Task<Result<object>> CreateOrderForUser(CreaateOrderDto dto)
        {
            var user = await _userRepository.GetById(dto.UserId);
            if (user is null)
                return Result<object>.Failure("کاربر یافت نشد");

            var car = await _carRepository.GetById(dto.CarId);
            if (car is null)
                return Result<object>.Failure("خودرو یافت نشد");

            var userRole = _userRoleRepository.GetAll()
                .FirstOrDefault(x => x.UserId == user.Id);

            if (userRole is null)
                return Result<object>.Failure("نقش کاربر یافت نشد");

            try
            {
                await unitOfWork.BeginTransactionAsync();

                var order = new Order
                {
                    UserId = user.Id,
                    CarId = car.Id,
                    RoleId = userRole.RoleId,
                    IsDelete = false,
                    CreateOrderDate = DateConvertor.GregorianToPersian(dto.CreateOrderDate)
                };

                await _orderRepository.Create(order);
                await unitOfWork.SaveChangesAsync();

                var baseDate = DateConvertor.PersianToGregorian(order.CreateOrderDate);

                for (int i = 0; i < dto.OrderItem.Time; i++)
                {
                    var installmentDateGregorian = baseDate.AddMonths(i + 1);
                    string installmentDatePersian = DateConvertor.GregorianToPersian(installmentDateGregorian);

                    var orderItem = new OrderItem
                    {
                        CarPrice = dto.OrderItem.CarPrice,
                        PrePayment = dto.OrderItem.PrePayment,
                        Time = dto.OrderItem.Time,
                        OrderId = order.Id,
                        IsDelete = false,
                        Price = _installmentCalculator.CalculateInstallment(
                                         dto.OrderItem.CarPrice,
                                         dto.OrderItem.PrePayment,
                                         dto.OrderItem.Time),

                        CreateOrderItemDate = installmentDatePersian
                    };

                    await _orderItemRepository.Create(orderItem);
                }

                await unitOfWork.CommitAsync();

                return Result<object>.Success(null, "سفارش با موفقیت ثبت شد");
            }
            catch
            {
                await unitOfWork.RollbackAsync();
                throw;
            }
        }

        public async Task<Result<List<OrderDto>>> GetUserOrders(Guid userId)
        {

            var orders =  await _orderRepository.GetAll()
                .Where(x=> x.UserId == userId)
                .Include(x => x.Car)
                .Include(x => x.User)
                .Include(x => x.User.UserRoles)
                .Include(x => x.Role)
                .Select(x => new OrderDto
                {
                    Id = x.Id,
                    CarName = x.Car.Name,
                    RoleName = x.Role.RoleName,
                    UserName = x.User.Name + "" + x.User.Family,
                    CreateOrderDate = x.CreateOrderDate
                })
                .ToListAsync();

            return Result<List<OrderDto>>.Success(orders);
        }
    }
}
