using Carshow.Utilities.Hasher;
using CarShow.ApplicationService.Contract.Dtos.RoleDto;
using CarShow.ApplicationService.Contract.Dtos.User;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using CarShow.Security.Token;
using Microsoft.EntityFrameworkCore;

namespace CarShow.ApplicationService.Services
{
    public class UserService : IUserService
    {
        private readonly IBaseRepository<Guid, User> _userRepository;
        private readonly IBaseRepository<long, UserRole> _userRoleRepository;
        private readonly IBaseRepository<long, Role> _roleRepository;
        private readonly ITokenGenerator _tokenGenerator;
        private readonly IBaseRepository<long, Order> orderRepository;
        private readonly IBaseRepository<long, OrderItem> orderItemRepository;
        private readonly IBaseRepository<long, Car> carRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IInstallmentCalculator _installmentCalculator;

        public UserService(
            IBaseRepository<Guid, User> userRepository,
            IBaseRepository<long, UserRole> userRoleRepository,
            IBaseRepository<long, Role> roleRepository,
            ITokenGenerator tokenGenerator,
            IBaseRepository<long, Order> orderRepository,
            IBaseRepository<long, OrderItem> orderItemRepository,
            IBaseRepository<long, Car> carRepository,
            IUnitOfWork unitOfWork,
            IInstallmentCalculator installmentCalculator)
        {
            _userRepository = userRepository;
            _userRoleRepository = userRoleRepository;
            _roleRepository = roleRepository;
            _tokenGenerator = tokenGenerator;
            this.orderRepository = orderRepository;
            this.orderItemRepository = orderItemRepository;
            this.carRepository = carRepository;
            _unitOfWork = unitOfWork;
            _installmentCalculator = installmentCalculator;
        }

        public async Task<Result<RegisterDto>> Register(RegisterDto dto)
        {
            var existingUser = _userRepository.GetAll()
                .FirstOrDefault(u => u.MobileNumber == dto.MobileNumber);

            if (existingUser != null)
                return Result<RegisterDto>
                    .Failure("کاربری با این شماره موبایل از قبل وجود دارد");

            var user = new User
            {
                Id = Guid.NewGuid(),
                MobileNumber = dto.MobileNumber,
                Password = PasswordHasher.HashPassword(dto.Password),
                IsDelete = false,
                Email = dto.Emaail,
                Name = dto.Name,
                Family = dto.Family,
            };

            try
            {
                await _unitOfWork.BeginTransactionAsync();

                await _userRepository.Create(user);
                await _userRepository.SaveChanges();

                await AddRoleToUser(user.Id, "Customer");

                await _unitOfWork.CommitAsync();
            }
            catch
            {
                await _unitOfWork.RollbackAsync();
                throw;
            }

            var outputDto = new RegisterDto
            {
                Id = user.Id,
                MobileNumber = user.MobileNumber,
                Password = null
            };

            return Result<RegisterDto>
                .Success(outputDto, "ثبت نام با موفقیت انجام شد");
        }



        public async Task AddRoleToUser(Guid userId, string roleName)
        {
            var role = _roleRepository.GetAll()
                .FirstOrDefault(r => r.RoleName == roleName);

            if (role == null)
                throw new Exception($"Role '{roleName}' یافت نشد");

            var existingUserRole = _userRoleRepository.GetAll()
                .FirstOrDefault(ur => ur.UserId == userId && ur.RoleId == role.Id);

            if (existingUserRole != null)
                return;

            var userRole = new UserRole
            {
                UserId = userId,
                RoleId = role.Id
            };

            await _userRoleRepository
                .Create(userRole);
            await _userRoleRepository
                .SaveChanges();
        }

        public async Task<Result<LoginDto>> Login(LoginDto dto)
        {
            var user = _userRepository.GetAll()
                .FirstOrDefault(u => u.MobileNumber == dto.MobileNumber);

            if (user == null || !PasswordHasher
                .VerifyPassword(dto.Password, user.Password))
                return Result<LoginDto>
                    .Failure("شماره موبایل یا رمز عبور اشتباه است");

            var roles = _userRoleRepository
                .GetAll()
                .Where(ur => ur.UserId == user.Id)
                .Join(_roleRepository.GetAll(),
                    ur => ur.RoleId,
                    r => r.Id,
                    (ur, r) => r)
                .ToList();

            var token = _tokenGenerator
                .GenerateToken(user, roles);
            dto.Token = token;

            return Result<LoginDto>.Success(dto, "ورود با موفقیت انجام شد");
        }

        public async Task<Result<object>> CreateUser(CreateUserDto dto)
        {
            var user = await _userRepository.GetAll()
                .FirstOrDefaultAsync(x => x.MobileNumber == dto.MobileNumber);
            if (user == null)
            {
                user = new User()
                {
                    Id = Guid.NewGuid(),
                    Name = dto.Name,
                    Email = dto.Email,
                    Family = dto.Family,
                    Password = PasswordHasher.HashPassword(dto.Password),
                    MobileNumber = dto.MobileNumber,
                };
                await _userRepository.Create(user);
                await _userRepository.SaveChanges();
                await AddRoleToUser(user.Id, dto.RoleName);
                return Result<object>.Success(dto, "ثبت کاربر با موفقیت انجام شد");
            }
            else
                return Result<object>.Failure("ثبت کاربر با خطا مواجه شد");
        }

        public async Task<Result<object>> UpdateUser(UpdateUserDto dto)
        {
            var user = await _userRepository.GetById(dto.Id);
            if (user != null)
            {
                user.Name = dto.Name;
                user.Email = dto.Email;
                user.Family = dto.Family;
                user.Password = PasswordHasher.HashPassword(dto.Password);
                user.MobileNumber = dto.MobileNumber;
                await _userRepository.Update(user);
                await _userRepository.SaveChanges();
                return Result<object>.Success(user, "ویرایش کاربر با موفقیت انجام شد");
            }
            else
                return Result<object>.Failure("ویرایش کاربر با خطا مواجه شد");

        }

        public async Task<Result<string>> DeleteUser(Guid id)
        {
            var user = await _userRepository.GetById(id);
            if (user != null)
            {
                await _userRepository.Delete(user);
                await _userRepository.SaveChanges();
                return Result<string>.Success("حذف کاربر با موفقیت انجام شد");
            }
            return Result<string>.Failure("حذف کاربر با خطا مواجه شد");
        }

        public List<UserDto> GetAllUsers(int pageSize = 10, int pageNumber = 0)
        {
            return _userRepository.GetAll().Select(x => new UserDto()
            {
                Id = x.Id,
                Name = x.Name,
                Email = x.Email,
                Family = x.Family,
                MobileNumber = x.MobileNumber,
            })
            .Skip(pageNumber * pageSize)
            .Take(pageSize)
            .ToList();
        }

        public List<RoleDto> GetAllRoles()
        {
            return _roleRepository.GetAll().Select(x => new RoleDto()
            {
                RoleId = x.Id,
                RoleName = x.RoleName
            }).ToList();
        }

   

    }
}