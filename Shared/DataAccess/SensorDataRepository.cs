﻿using System.Data.Entity;
using System.Linq;
using Shared.Entities;

namespace Shared.DataAccess
{
    public class SensorDataRepository
    {
        DataContext _context;

        public SensorDataRepository()
        {
            _context = new DataContext();
        }

        public SensorData LastEntrie()
        {
            return _context.SensorData
                .OrderByDescending(x => x.DateTime)
                .Take(1)
                .FirstOrDefault();
        }

        public void Add(SensorData data)
        {
            _context.SensorData.Add(data);
            _context.SaveChanges();
        }
    }
}
