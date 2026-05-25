SELECT 'CREATE DATABASE nerv_iip_apphub'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_apphub')\gexec
SELECT 'CREATE DATABASE nerv_iip_iam'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_iam')\gexec
SELECT 'CREATE DATABASE nerv_iip_ops'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_ops')\gexec
SELECT 'CREATE DATABASE nerv_iip_filestorage'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_filestorage')\gexec
SELECT 'CREATE DATABASE nerv_iip_notification'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_notification')\gexec
SELECT 'CREATE DATABASE nerv_iip_business_masterdata'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_business_masterdata')\gexec
SELECT 'CREATE DATABASE nerv_iip_product_engineering'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_product_engineering')\gexec
SELECT 'CREATE DATABASE nerv_iip_inventory'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_inventory')\gexec
SELECT 'CREATE DATABASE nerv_iip_quality'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_quality')\gexec
SELECT 'CREATE DATABASE nerv_iip_mes'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_mes')\gexec
SELECT 'CREATE DATABASE nerv_iip_demand_planning'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_demand_planning')\gexec
SELECT 'CREATE DATABASE nerv_iip_barcode'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_barcode')\gexec
SELECT 'CREATE DATABASE nerv_iip_business_approval'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_business_approval')\gexec
SELECT 'CREATE DATABASE nerv_iip_wms'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_wms')\gexec
SELECT 'CREATE DATABASE nerv_iip_industrial_telemetry'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_industrial_telemetry')\gexec
SELECT 'CREATE DATABASE nerv_iip_maintenance'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_maintenance')\gexec
SELECT 'CREATE DATABASE nerv_iip_erp'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nerv_iip_erp')\gexec
