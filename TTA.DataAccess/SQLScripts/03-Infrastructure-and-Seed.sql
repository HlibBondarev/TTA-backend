-- ==========================================
-- 1. GEOGRAPHY
-- ==========================================

DO $$ 
DECLARE 
    v_ukraine_id INT;
BEGIN
    INSERT INTO countries (name, code) 
    VALUES ('Ukraine', 'UKR')
    ON CONFLICT (code) DO NOTHING;

    SELECT id INTO v_ukraine_id FROM countries WHERE code = 'UKR';

    INSERT INTO regions (countryid, name) VALUES 
    (v_ukraine_id, 'Lviv Oblast'), 
    (v_ukraine_id, 'Kyiv City'), 
    (v_ukraine_id, 'Kyiv Oblast'), 
    (v_ukraine_id, 'Kharkiv Oblast'), 
    (v_ukraine_id, 'Dnipropetrovsk Oblast'), 
    (v_ukraine_id, 'Odessa Oblast'), 
    (v_ukraine_id, 'Donetsk Oblast'), 
    (v_ukraine_id, 'Zakarpattia Oblast')
    ON CONFLICT (countryid, name) DO NOTHING;

    INSERT INTO cities (id, regionid, name) VALUES 
    ('c0000000-0000-0000-0000-000000000001', (SELECT id FROM regions WHERE name = 'Lviv Oblast' AND countryid = v_ukraine_id), 'Lviv'),
    ('c0000000-0000-0000-0000-000000000002', (SELECT id FROM regions WHERE name = 'Kyiv City' AND countryid = v_ukraine_id), 'Kyiv'),
    ('c0000000-0000-0000-0000-000000000003', (SELECT id FROM regions WHERE name = 'Kyiv Oblast' AND countryid = v_ukraine_id), 'Brovary'),
    ('c0000000-0000-0000-0000-000000000004', (SELECT id FROM regions WHERE name = 'Kharkiv Oblast' AND countryid = v_ukraine_id), 'Kharkiv'),
    ('c0000000-0000-0000-0000-000000000005', (SELECT id FROM regions WHERE name = 'Dnipropetrovsk Oblast' AND countryid = v_ukraine_id), 'Dnipro'),
    ('c0000000-0000-0000-0000-000000000006', (SELECT id FROM regions WHERE name = 'Odessa Oblast' AND countryid = v_ukraine_id), 'Odessa'),
    ('c0000000-0000-0000-0000-000000000007', (SELECT id FROM regions WHERE name = 'Donetsk Oblast' AND countryid = v_ukraine_id), 'Mariupol'),
    ('c0000000-0000-0000-0000-000000000008', (SELECT id FROM regions WHERE name = 'Donetsk Oblast' AND countryid = v_ukraine_id), 'Kramatorsk'),
    ('c0000000-0000-0000-0000-000000000009', (SELECT id FROM regions WHERE name = 'Zakarpattia Oblast' AND countryid = v_ukraine_id), 'Uzhhorod')
    ON CONFLICT (regionid, name) DO NOTHING;

    RAISE NOTICE 'Geography seeding completed successfully.';
END $$;

-- ==========================================
-- 2. SPORT DEFINITION
-- ==========================================

DO $$ 
DECLARE 
    sport_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f';
BEGIN
    INSERT INTO sports (id, name) 
    VALUES (sport_id, 'Water Polo')
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO playerpositiondefinitions (id, sportid, name, shortname) VALUES 
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f61', sport_id, 'Goalkeeper', 'GK'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f62', sport_id, 'Center Forward', 'CF'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f63', sport_id, 'Center Back', 'CB'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f64', sport_id, 'Driver', 'D'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', sport_id, 'Wing', 'W'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f66', sport_id, 'Utility', 'UTL')
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit)
    VALUES ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f70', sport_id, true, 4, 8, '25x20m', 15, 13)
    ON CONFLICT (id) DO NOTHING;

-- ==========================================
-- 3. TECHNICAL & TACTICAL ACTIONS
-- ==========================================

    INSERT INTO eventdefinitions (id, sportid, name, shortname, ispositive, createdat) VALUES 
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f81', sport_id, 'Goal', 'GOAL', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f82', sport_id, 'Assist', 'ASST', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f83', sport_id, 'Sprint Won', 'SPR+', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f84', sport_id, 'Exclusion Earned', 'EXCL+', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f85', sport_id, 'Penalty Earned', 'PEN+', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f86', sport_id, 'Steal', 'STL', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f87', sport_id, 'Shot Saved', 'SAVE', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f88', sport_id, 'Block', 'BLK', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f89', sport_id, 'Shot Missed', 'MISS', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f90', sport_id, 'Turnover', 'T-OVER', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f91', sport_id, 'Exclusion Received', 'EXCL-', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f92', sport_id, 'Penalty Committed', 'PEN-', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f93', sport_id, 'Sprint Lost', 'SPR-', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f94', sport_id, 'Critical Foul', 'C-FOUL', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f95', sport_id, 'Bad Goal Conceded', 'B-GOAL', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f96', sport_id, 'Tactical Error', 'T-ERR', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f97', sport_id, 'Defensive Transition Failure', 'D-TRANS', false, NOW())
    ON CONFLICT (id) DO NOTHING;
END $$;

-- ==========================================
-- 4. CLUBS & TEAMS
-- ==========================================

INSERT INTO clubs (id, cityid, name, createdat) VALUES 
('11111111-1111-1111-1111-111111111101', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Lviv' AND r.name = 'Lviv Oblast' AND co.code = 'UKR' LIMIT 1), 'Dynamo Lviv', NOW()),
('11111111-1111-1111-1111-111111111102', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Mariupol' AND r.name = 'Donetsk Oblast' AND co.code = 'UKR' LIMIT 1), 'Mariupol', NOW()),
('11111111-1111-1111-1111-111111111103', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Kharkiv' AND r.name = 'Kharkiv Oblast' AND co.code = 'UKR' LIMIT 1), 'NTU-KhPI Kharkiv', NOW()),
('11111111-1111-1111-1111-111111111107', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Kyiv' AND r.name = 'Kyiv City' AND co.code = 'UKR' LIMIT 1), 'Kyiv City Team', NOW()),
('11111111-1111-1111-1111-111111111109', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Lviv' AND r.name = 'Lviv Oblast' AND co.code = 'UKR' LIMIT 1), 'Dynamo-Amazonky Lviv', NOW()),
('11111111-1111-1111-1111-111111111110', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Dnipro' AND r.name = 'Dnipropetrovsk Oblast' AND co.code = 'UKR' LIMIT 1), 'My super club', NOW()),
('11111111-1111-1111-1111-111111111111', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Dnipro' AND r.name = 'Dnipropetrovsk Oblast' AND co.code = 'UKR' LIMIT 1), 'My super club - 2', NOW())
ON CONFLICT (id) DO NOTHING;

DO $$ 
DECLARE 
    v_wp_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f';
BEGIN
    INSERT INTO teams (id, clubid, sportid, name, minbirthyear, gender, createdat) VALUES 
    ('22222222-2222-2222-2222-222222222201', '11111111-1111-1111-1111-111111111101', v_wp_id, 'Dynamo Lviv (Men)', NULL, 0, NOW()), -- CHANGED: Male -> 0
    ('22222222-2222-2222-2222-222222222202', '11111111-1111-1111-1111-111111111102', v_wp_id, 'SHVSM Mariupol (Men)', NULL, 0, NOW()), -- CHANGED: Male -> 0
    ('22222222-2222-2222-2222-222222222203', '11111111-1111-1111-1111-111111111103', v_wp_id, 'NTU-KhPI - SHVSM (Men)', NULL, 0, NOW()), -- CHANGED: Male -> 0
    ('22222222-2222-2222-2222-222222222213', '11111111-1111-1111-1111-111111111101', v_wp_id, 'Dynamo Lviv U-13 (2013)', 2013, 0, NOW()), -- CHANGED: Male -> 0
    ('22222222-2222-2222-2222-222222222209', '11111111-1111-1111-1111-111111111107', v_wp_id, 'Kyiv U-15 (2011)', 2011, 0, NOW()), -- CHANGED: Male -> 0
    ('22222222-2222-2222-2222-222222222204', '11111111-1111-1111-1111-111111111109', v_wp_id, 'Dynamo-Amazonky (Women)', NULL, 1, NOW()), -- CHANGED: Female -> 1
    ('22222222-2222-2222-2222-222222222211', '11111111-1111-1111-1111-111111111107', v_wp_id, 'Kyiv City Selection (Women)', NULL, 1, NOW()), -- CHANGED: Female -> 1
    ('22222222-2222-2222-2222-222222222215', '11111111-1111-1111-1111-111111111110', v_wp_id, 'My super team U-15 (2011)', 2011, 0, NOW()), -- CHANGED: Male -> 0
    ('22222222-2222-2222-2222-222222222216', '11111111-1111-1111-1111-111111111111', v_wp_id, 'My super team - 2 U-15 (2011)', 2011, 0, NOW()) -- CHANGED: Male -> 0
    ON CONFLICT (id) DO NOTHING;
END $$;

-- ==========================================
-- 5. OAuth CHECK & PERMISSIONS
-- ==========================================

DO $$ 
DECLARE 
    v_user_id VARCHAR := 'auth0|69cf7ec5eff8f1358a0b9ae0'; 
    v_club_id UUID := '11111111-1111-1111-1111-111111111110';
    v_team_id UUID := '22222222-2222-2222-2222-222222222215'; 
    v_user_id_2 VARCHAR := 'auth0|698b956080889e5401cef7c5'; 
    v_club_id_2 UUID := '11111111-1111-1111-1111-111111111111';
    v_team_id_2 UUID := '22222222-2222-2222-2222-222222222216'; 
    v_user_id_3 VARCHAR := 'auth0|698b9bd69f764e2999518960';
BEGIN
    -- 1. Ensure user exists
    -- Note: In a real application, users would be created via the Auth0 integration flow. This is just for seeding purposes.
    -- Hlib Bondarev
    INSERT INTO public.users (id, email, displayname, createdat)
    VALUES (v_user_id, 'hlib.bondarev@gmail.com', 'Hlib Bondarev', NOW())
    ON CONFLICT (id) DO NOTHING;
    -- Taras Shevchenko
    INSERT INTO public.users (id, email, displayname, createdat)
    VALUES (v_user_id_2, 'user1@example.com', 'Taras Shevchenko', NOW())
    ON CONFLICT (id) DO NOTHING;
    -- User without team - Ivan Franko (Admin)
    INSERT INTO public.users (id, email, displayname, createdat)
    VALUES (v_user_id_3, 'user2@example.com', 'Ivan Franko', NOW())
    ON CONFLICT (id) DO NOTHING;

    -- 2. Existing Team Membership
    -- for Hlib Bondarev and "My super team U-15 (2011)"
    INSERT INTO public.teammemberships (id, userid, teamid, roleinteam, joinedat, isprimary)
    SELECT '88888888-8888-8888-8888-888888888801', v_user_id, v_team_id, 0, NOW(), true -- roleinteam: HeadCoach -> 0
    WHERE NOT EXISTS (
        SELECT 1 FROM public.teammemberships 
        WHERE userid = v_user_id AND teamid = v_team_id
    );
    -- for Taras Shevchenko and "My super team - 2 U-15 (2011)"
    INSERT INTO public.teammemberships (id, userid, teamid, roleinteam, joinedat, isprimary)
    SELECT '88888888-8888-8888-8888-888888888802', v_user_id_2, v_team_id_2, 0, NOW(), true -- roleinteam: HeadCoach -> 0
    WHERE NOT EXISTS (
        SELECT 1 FROM public.teammemberships 
        WHERE userid = v_user_id_2 AND teamid = v_team_id_2
    );

    -- 3. Policy for the Clubs & Teams & Global (Admin) - ensuring no duplicates

    -- for Hlib Bondarev and "My super club"
    INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat)
    SELECT '99999999-9999-9999-9999-999999999900', v_user_id, 0, 1, v_club_id, NOW() -- role: FullControl -> 0, targettype: Club -> 1
    WHERE NOT EXISTS (
        SELECT 1 FROM auth.accesspolicies 
        WHERE userid = v_user_id AND targettype = 1 AND targetid = v_club_id -- targettype: Club -> 1
    );
    RAISE NOTICE 'Seed completed: User % granted FullControl over Club %', v_user_id, v_club_id;

    -- for Hlib Bondarev and "My super team U-15 (2011)"
    INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat)
    SELECT '99999999-9999-9999-9999-999999999901', v_user_id, 0, 2, v_team_id, NOW() -- role: FullControl -> 0, targettype: Team -> 2
    WHERE NOT EXISTS (
        SELECT 1 FROM auth.accesspolicies 
        WHERE userid = v_user_id AND targettype = 2 AND targetid = v_team_id -- targettype: Team -> 2
    );
    RAISE NOTICE 'Seed completed: User % granted FullControl over Team %', v_user_id, v_team_id;

    -- for Taras Shevchenko and "My super club - 2 U-15 (2011)"
    INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat)
    SELECT '99999999-9999-9999-9999-999999999902', v_user_id_2, 0, 1, v_club_id_2, NOW() -- role: FullControl -> 0, targettype: Club -> 1
    WHERE NOT EXISTS (
        SELECT 1 FROM auth.accesspolicies 
        WHERE userid = v_user_id_2 AND targettype = 1 AND targetid = v_club_id_2 -- targettype: Club -> 1
    );
    RAISE NOTICE 'Seed completed: User % granted FullControl over Club %', v_user_id_2, v_club_id_2;

    -- for Taras Shevchenko and "My super team - 2 U-15 (2011)"
    INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat)
    SELECT '99999999-9999-9999-9999-999999999903', v_user_id_2, 0, 2, v_team_id_2, NOW() -- role: FullControl -> 0, targettype: Team -> 2
    WHERE NOT EXISTS (
        SELECT 1 FROM auth.accesspolicies 
        WHERE userid = v_user_id_2 AND targettype = 2 AND targetid = v_team_id_2 -- targettype: Team -> 2
    );
    RAISE NOTICE 'Seed completed: User % granted FullControl over Team %', v_user_id_2, v_team_id_2;

    -- for Ivan Franko (Admin)
    INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat)
    SELECT '99999999-9999-9999-9999-999999999904', v_user_id_3, 0, 0, NULL, NOW() -- role: FullControl -> 0, targettype: Global -> 0
    WHERE NOT EXISTS (
        SELECT 1 FROM auth.accesspolicies 
        WHERE userid = v_user_id_3 AND targettype = 0 -- targettype: Global -> 0
    );
    RAISE NOTICE 'Seed completed: User % granted FullControl over Global', v_user_id_3;

END $$;

-- ===========================================================
-- 6. PLAYERS (Seed for "My super club" and "My super club 2")
-- ===========================================================

DO $$ 
DECLARE 
    v_club_id uuid := '11111111-1111-1111-1111-111111111110';
    v_club_id_2 uuid := '11111111-1111-1111-1111-111111111111';
BEGIN
    INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES 
    ('33333333-3333-3333-3333-333333333001', v_club_id, 'Олександр', 'Коваленко', '2011-05-15', 0, NOW()),
    ('33333333-3333-3333-3333-333333333002', v_club_id, 'Максим', 'Бондар', '2011-08-22', 0, NOW()), 
    ('33333333-3333-3333-3333-333333333003', v_club_id, 'Артем', 'Шевченко', '2012-01-10', 0, NOW()),
    ('33333333-3333-3333-3333-333333333004', v_club_id, 'Дмитро', 'Марченко', '2011-11-30', 0, NOW()),
    ('33333333-3333-3333-3333-333333333005', v_club_id, 'Іван', 'Сидоренко', '2011-03-05', 0, NOW()),
    ('33333333-3333-3333-3333-333333333006', v_club_id, 'Сергій', 'Кравченко', '2012-07-18', 0, NOW()),
    ('33333333-3333-3333-3333-333333333007', v_club_id, 'Володимир', 'Григоренко', '2011-09-25', 0, NOW()),
    ('33333333-3333-3333-3333-333333333008', v_club_id, 'Павло', 'Левченко', '2011-12-12', 0, NOW()),
    ('33333333-3333-3333-3333-333333333009', v_club_id, 'Юрій', 'Козак', '2012-02-20', 0, NOW()),
    ('33333333-3333-3333-3333-333333333010', v_club_id, 'Андрій', 'Мельник', '2011-06-30', 0, NOW()),
    ('33333333-3333-3333-3333-333333333011', v_club_id, 'Олег', 'Федоренко', '2011-04-18', 0, NOW()),
    ('33333333-3333-3333-3333-333333333012', v_club_id, 'Євген', 'Гончаренко', '2012-09-05', 0, NOW()),
    ('33333333-3333-3333-3333-333333333013', v_club_id, 'Микола', 'Соловйов', '2011-10-22', 0, NOW()),
    ('33333333-3333-3333-3333-333333333014', v_club_id, 'Віталій', 'Даниленко', '2011-07-14', 0, NOW()),
    ('33333333-3333-3333-3333-333333333015', v_club_id, 'Григорій', 'Петренко', '2012-03-28', 0, NOW()),
    ('33333333-3333-3333-3333-333333333016', v_club_id_2, 'Степан', 'Климченко', '2011-05-10', 0, NOW()),
    ('33333333-3333-3333-3333-333333333017', v_club_id_2, 'Олексій', 'Грищенко', '2011-08-05', 0, NOW()),
    ('33333333-3333-3333-3333-333333333018', v_club_id_2, 'Василь', 'Морозенко', '2012-01-20', 0, NOW()),
    ('33333333-3333-3333-3333-333333333019', v_club_id_2, 'Ігор', 'Ткаченко', '2011-11-15', 0, NOW()),
    ('33333333-3333-3333-3333-333333333020', v_club_id_2, 'Семен', 'Ковальчук', '2011-03-30', 0, NOW()),
    ('33333333-3333-3333-3333-333333333021', v_club_id_2, 'Михайло', 'Григорьєв', '2011-09-12', 0, NOW()),
    ('33333333-3333-3333-3333-333333333022', v_club_id_2, 'Роман', 'Лисенко', '2012-04-25', 0, NOW()),
    ('33333333-3333-3333-3333-333333333023', v_club_id_2, 'Владислав', 'Кравчук', '2011-06-05', 0, NOW()),
    ('33333333-3333-3333-3333-333333333024', v_club_id_2, 'Петро', 'Сидоренко', '2011-12-20', 0, NOW()),
    ('33333333-3333-3333-3333-333333333025', v_club_id_2, 'Остап', 'Григоренко', '2012-02-10', 0, NOW()),
    ('33333333-3333-3333-3333-333333333026', v_club_id_2, 'Денис', 'Мельник', '2011-07-30', 0, NOW()),
    ('33333333-3333-3333-3333-333333333027', v_club_id_2, 'Віктор', 'Федоренко', '2011-04-05', 0, NOW()),
    ('33333333-3333-3333-3333-333333333028', v_club_id_2, 'Єгор', 'Гончаренко', '2012-09-20', 0, NOW()),
    ('33333333-3333-3333-3333-333333333029', v_club_id_2, 'Марк', 'Соловйов', '2011-10-05', 0, NOW()),
    ('33333333-3333-3333-3333-333333333030', v_club_id_2, 'Володимир', 'Даниленко', '2011-07-01', 0, NOW())
    ON CONFLICT (id) DO NOTHING;

    RAISE NOTICE 'Seed for 30 players in "My super club" and "My super club 2" completed successfully.';
END $$;

-- ==========================================
-- 7. TOURNAMENTS
-- ==========================================

DO $$ 
DECLARE 
    -- References from existing seeds
    v_sport_id UUID := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f'; -- Water Polo
    v_config_id UUID := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f70'; -- Default WP Config
    v_city_lviv UUID := 'c0000000-0000-0000-0000-000000000001';
    v_city_kyiv UUID := 'c0000000-0000-0000-0000-000000000002';
    v_city_dnipro UUID := 'c0000000-0000-0000-0000-000000000005';
    -- Existing Users
    v_user_hlib VARCHAR := 'auth0|69cf7ec5eff8f1358a0b9ae0';
    v_user_taras VARCHAR := 'auth0|698b956080889e5401cef7c5';
    v_user_ivan VARCHAR := 'auth0|698b9bd69f764e2999518960';
    v_tournament_hlib UUID := 'c0000000-0000-0000-1111-000000000001';
    v_tournament_taras UUID := 'c0000000-0000-0000-1111-000000000002';
    v_tournament_ivan UUID := 'c0000000-0000-0000-1111-000000000003';

BEGIN
    -- 1. Tournament for Hlib Bondarev (In 1 month)
    INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, enddate, createdat)
    VALUES (
        v_tournament_hlib, 
        v_sport_id, 
        v_config_id, 
        v_city_lviv, 
        v_user_hlib, 
        'Spring Water Polo Cup 2026', 
        CURRENT_DATE + INTERVAL '1 month', 
        CURRENT_DATE + INTERVAL '1 month' + INTERVAL '3 days', 
        NOW()
    ) ON CONFLICT DO NOTHING;

    -- 2. Tournament for Taras Shevchenko (In 3 months)
    INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, enddate, createdat)
    VALUES (
        v_tournament_taras, 
        v_sport_id, 
        v_config_id, 
        v_city_kyiv, 
        v_user_taras, 
        'Summer Kyiv Invitational', 
        CURRENT_DATE + INTERVAL '3 months', 
        CURRENT_DATE + INTERVAL '3 months' + INTERVAL '5 days', 
        NOW()
    ) ON CONFLICT DO NOTHING;

    -- 3. Tournament for Ivan Franko (In 6 months)
    INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, enddate, createdat)
    VALUES (
        v_tournament_ivan, 
        v_sport_id, 
        v_config_id, 
        v_city_dnipro, 
        v_user_ivan, 
        'Autumn Championship Dnipro', 
        CURRENT_DATE + INTERVAL '6 months', 
        CURRENT_DATE + INTERVAL '6 months' + INTERVAL '7 days', 
        NOW()
    ) ON CONFLICT DO NOTHING;

    RAISE NOTICE 'Tournaments seeding completed successfully.';
END $$;

-- ==============================================================================================================================
-- 8. PLAYERROSTERS (Seed for Hlib Bondarev's tournament, teams - "My super team U-15 (2011)" and "My super team - 2 U-15 (2011)"
-- ==============================================================================================================================

DO $$ 
DECLARE 
    v_tournament_id uuid;
    v_team_id uuid := '22222222-2222-2222-2222-222222222215';
    v_team_id_2 uuid := '22222222-2222-2222-2222-222222222216';
BEGIN

    SELECT id INTO v_tournament_id
    FROM public.tournaments
    WHERE name = 'Spring Water Polo Cup 2026';
    IF v_tournament_id IS NULL THEN
        RAISE EXCEPTION 'Tournament "Spring Water Polo Cup 2026" not found; ensure the tournaments seed ran first.';
    END IF;

    -- Insert players into the roster using a subquery to ensure idempotency.
    -- This checks both the (tournament, player) and (tournament, team, number) unique constraints.
    INSERT INTO public.playerrosters (id, playerid, tournamentid, teamid, number, positionid, createdat)
    SELECT id, player_id, t_id, tm_id, p_num, pos_id, created
    FROM (VALUES
    ('33333333-3333-3333-0000-333333333001'::UUID, '33333333-3333-3333-3333-333333333001'::UUID, v_tournament_id, v_team_id, 1, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f61'::UUID, NOW()),
    ('33333333-3333-3333-0000-333333333002', '33333333-3333-3333-3333-333333333002', v_tournament_id, v_team_id, 2, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', NOW()),
    ('33333333-3333-3333-0000-333333333003', '33333333-3333-3333-3333-333333333003', v_tournament_id, v_team_id, 3, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', NOW()),
    ('33333333-3333-3333-0000-333333333004', '33333333-3333-3333-3333-333333333004', v_tournament_id, v_team_id, 4, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', NOW()),
    ('33333333-3333-3333-0000-333333333005', '33333333-3333-3333-3333-333333333005', v_tournament_id, v_team_id, 5, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', NOW()),
    ('33333333-3333-3333-0000-333333333006', '33333333-3333-3333-3333-333333333006', v_tournament_id, v_team_id, 6, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', NOW()),
    ('33333333-3333-3333-0000-333333333007', '33333333-3333-3333-3333-333333333007', v_tournament_id, v_team_id, 7, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f62', NOW()),
    ('33333333-3333-3333-0000-333333333008', '33333333-3333-3333-3333-333333333008', v_tournament_id, v_team_id, 8, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f62', NOW()),
    ('33333333-3333-3333-0000-333333333009', '33333333-3333-3333-3333-333333333009', v_tournament_id, v_team_id, 9, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f63', NOW()),
    ('33333333-3333-3333-0000-333333333010', '33333333-3333-3333-3333-333333333010', v_tournament_id, v_team_id, 10, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f63', NOW()),
    ('33333333-3333-3333-0000-333333333011', '33333333-3333-3333-3333-333333333011', v_tournament_id, v_team_id, 11, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f64', NOW()),
    ('33333333-3333-3333-0000-333333333012', '33333333-3333-3333-3333-333333333012', v_tournament_id, v_team_id, 12, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f64', NOW()),
    ('33333333-3333-3333-0000-333333333013', '33333333-3333-3333-3333-333333333013', v_tournament_id, v_team_id, 13, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f66', NOW()),
    ('33333333-3333-3333-0000-333333333014', '33333333-3333-3333-3333-333333333014', v_tournament_id, v_team_id, 14, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f66', NOW()),
    ('33333333-3333-3333-0000-333333333015', '33333333-3333-3333-3333-333333333015', v_tournament_id, v_team_id, 15, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f61', NOW()),
    ('33333333-3333-3333-0000-333333333016', '33333333-3333-3333-3333-333333333016', v_tournament_id, v_team_id_2, 1, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f61', NOW()),
    ('33333333-3333-3333-0000-333333333017', '33333333-3333-3333-3333-333333333017', v_tournament_id, v_team_id_2, 2, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', NOW()),
    ('33333333-3333-3333-0000-333333333018', '33333333-3333-3333-3333-333333333018', v_tournament_id, v_team_id_2, 3, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', NOW()),
    ('33333333-3333-3333-0000-333333333019', '33333333-3333-3333-3333-333333333019', v_tournament_id, v_team_id_2, 4, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', NOW()),
    ('33333333-3333-3333-0000-333333333020', '33333333-3333-3333-3333-333333333020', v_tournament_id, v_team_id_2, 5, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', NOW()),
    ('33333333-3333-3333-0000-333333333021', '33333333-3333-3333-3333-333333333021', v_tournament_id, v_team_id_2, 6, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', NOW()),
    ('33333333-3333-3333-0000-333333333022', '33333333-3333-3333-3333-333333333022', v_tournament_id, v_team_id_2, 7, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f62', NOW()),
    ('33333333-3333-3333-0000-333333333023', '33333333-3333-3333-3333-333333333023', v_tournament_id, v_team_id_2, 8, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f62', NOW()),
    ('33333333-3333-3333-0000-333333333024', '33333333-3333-3333-3333-333333333024', v_tournament_id, v_team_id_2, 9, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f63', NOW()),
    ('33333333-3333-3333-0000-333333333025', '33333333-3333-3333-3333-333333333025', v_tournament_id, v_team_id_2, 10, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f63', NOW()),
    ('33333333-3333-3333-0000-333333333026', '33333333-3333-3333-3333-333333333026', v_tournament_id, v_team_id_2, 11, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f64', NOW()),
    ('33333333-3333-3333-0000-333333333027', '33333333-3333-3333-3333-333333333027', v_tournament_id, v_team_id_2, 12, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f64', NOW()),
    ('33333333-3333-3333-0000-333333333028', '33333333-3333-3333-3333-333333333028', v_tournament_id, v_team_id_2, 13, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f66', NOW()),
    ('33333333-3333-3333-0000-333333333029', '33333333-3333-3333-3333-333333333029', v_tournament_id, v_team_id_2, 14, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f66', NOW()),
    ('33333333-3333-3333-0000-333333333030', '33333333-3333-3333-3333-333333333030', v_tournament_id, v_team_id_2, 15, '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f61', NOW())
    ) AS seed(id, player_id, t_id, tm_id, p_num, pos_id, created)
    WHERE NOT EXISTS (
        SELECT 1 FROM public.playerrosters pr
        WHERE (pr.tournamentid = seed.t_id AND pr.playerid = seed.player_id) -- Constraint 1
           OR (pr.tournamentid = seed.t_id AND pr.teamid = seed.tm_id AND pr.number = seed.p_num) -- Constraint 2
    )
    AND NOT EXISTS (SELECT 1 FROM public.playerrosters WHERE id = seed.id); -- Primary Key Check

    RAISE NOTICE 'Seed for 30 players for "Spring Water Polo Cup 2026" (Bondarev) tournament, teams - "My super team U-15 (2011)" and "My super team - 2 U-15 (2011)"';
END $$;

-- ====================================================================================================================================================
-- 9. MATCHES (Seed for Hlib Bondarev's tournament - 'Spring Water Polo Cup 2026', match: "My super team U-15 (2011)" - "My super team - 2 U-15 (2011)"
-- ====================================================================================================================================================
-- MATCHES SEEDING:
-- Purpose: Seeds initial matches for 'Spring Water Polo Cup 2026' tournament.
-- Constraints: 
--   1. Ensures the tournament exists.
--   2. Ensures BOTH home and guest teams have at least one player in their respective rosters for this tournament.
--   3. Prevents duplicate entries via Primary Key check.

DO $$ 
DECLARE 
    v_tournament_id uuid;
    v_team_id uuid := '22222222-2222-2222-2222-222222222215';
    v_team_id_2 uuid := '22222222-2222-2222-2222-222222222216';
    v_match_number_1 VARCHAR := 'A-1';
    v_match_number_2 VARCHAR := 'A-2';
BEGIN

    -- Retrieve tournament ID by name
    SELECT id INTO v_tournament_id
    FROM public.tournaments
    WHERE name = 'Spring Water Polo Cup 2026';

    -- Validation: Ensure the tournament exists before proceeding
    IF v_tournament_id IS NULL THEN
        RAISE EXCEPTION 'Tournament "Spring Water Polo Cup 2026" not found; ensure the tournaments seed ran first.';
    END IF;

    -- Insert matches into the matches table using a subquery for idempotent seeding.
    INSERT INTO public.matches (
        id, 
        tournamentid, 
        hometeamid, 
        guestteamid, 
        scheduledat, 
        matchnumber, 
        venue, 
        temperature, 
        homescore, 
        guestscore, 
        createdat
    )
    SELECT id, tourn_id, ht_id, gt_id, scheduled, m_number, venue, temper, hmscore, gtscore, created
    FROM (VALUES
        ('33333333-3333-0000-0000-333333333001'::UUID, v_tournament_id, v_team_id, v_team_id_2, CURRENT_DATE + INTERVAL '1 month', v_match_number_1, 'Central Arena', 25, 14, 10, NOW()),
        ('33333333-3333-0000-0000-333333333002'::UUID, v_tournament_id, v_team_id_2, v_team_id, CURRENT_DATE + INTERVAL '1 month' + INTERVAL '1 day', v_match_number_2, 'MiKomp', 26, 15, 16, NOW())
    ) AS seed(id, tourn_id, ht_id, gt_id, scheduled, m_number, venue, temper, hmscore, gtscore, created)
    WHERE 
        -- Requirement: The home team must have a registered roster in this tournament
        EXISTS (
            SELECT 1 FROM public.playerrosters pr
            WHERE pr.tournamentid = seed.tourn_id AND pr.teamid = seed.ht_id
        )
        -- Requirement: The guest team must have a registered roster in this tournament
        AND EXISTS (
            SELECT 1 FROM public.playerrosters pr
            WHERE pr.tournamentid = seed.tourn_id AND pr.teamid = seed.gt_id
        )
        -- Idempotency: Ensure the match record does not already exist
        AND NOT EXISTS (
            SELECT 1 FROM public.matches WHERE id = seed.id
        );

    RAISE NOTICE 'Seeding completed for "Spring Water Polo Cup 2026" matches.';
END $$;

-- ========================================================================================================================================
-- 10. Seed lineups for match 33333333-3333-0000-0000-333333333001
-- ========================================================================================================================================
-- MATCH of Hlib Bondarev's tournament - 'Spring Water Polo Cup 2026', match: "My super team U-15 (2011)" - "My super team - 2 U-15 (2011)"
DO $$ 
DECLARE 
    v_match_id uuid := '33333333-3333-0000-0000-333333333001'; 
    v_team_home uuid := '22222222-2222-2222-2222-222222222215';
    v_team_guest uuid := '22222222-2222-2222-2222-222222222216';
    v_home_players uuid[];
    v_guest_players uuid[];
BEGIN
    -- Collect up to 13 player IDs for home team
    SELECT array_agg(id) INTO v_home_players
    FROM (
        SELECT id FROM public.playerrosters 
        WHERE teamid = v_team_home
        ORDER BY id
        LIMIT 13
    ) AS sub;

    -- Collect up to 13 player IDs for guest team
    SELECT array_agg(id) INTO v_guest_players
    FROM (
        SELECT id FROM public.playerrosters 
        WHERE teamid = v_team_guest
        ORDER BY id
        LIMIT 13
    ) AS sub;

    -- Execute copy with specific arrays
    PERFORM public.copy_team_roster_to_match_lineup(v_match_id, v_team_home, v_home_players);
    PERFORM public.copy_team_roster_to_match_lineup(v_match_id, v_team_guest, v_guest_players);

    -- Set starting 7
    UPDATE public.matchlineups 
    SET isinstartinglineup = true 
    WHERE matchid = v_match_id 
      AND number > 0
      AND number <= 7;

    RAISE NOTICE 'Lineups for match % initialized with specific selection.', v_match_id;
END $$;

-- ==============================================================================
-- 11. SEED TIME ANCHORS FOR MATCH 33333333-3333-0000-0000-333333333001
-- ==============================================================================
-- Enforces chronological timeline boundaries for Period 1 and Period 2.
-- Period 1 contains a 2-minute stoppage window (StoppageStart to StoppageEnd).
-- Total real duration: 12 mins. Effective play duration: 10 mins.
-- Fixed: Replaced random UUIDs with deterministic IDs to guarantee idempotency.
-- ==============================================================================
DO $$
DECLARE
    v_match_id uuid := '33333333-3333-0000-0000-333333333001';
    v_base_time timestamptz;
BEGIN
    -- Extract the relative match scheduled time to prevent chronological drift
    SELECT scheduledat INTO v_base_time FROM public.matches WHERE id = v_match_id;

    INSERT INTO public.timeanchors (id, matchid, periodnumber, type, timestamp) VALUES
    -- Period 1 Chronology (Types: 0=PeriodStart, 1=PeriodEnd, 2=StoppageStart, 3=StoppageEnd)
    ('44444444-4444-0000-0001-000000000001', v_match_id, 1, 0, v_base_time),
    ('44444444-4444-0000-0001-000000000002', v_match_id, 1, 2, v_base_time + interval '4 minutes'),
    ('44444444-4444-0000-0001-000000000003', v_match_id, 1, 3, v_base_time + interval '6 minutes'),
    ('44444444-4444-0000-0001-000000000004', v_match_id, 1, 1, v_base_time + interval '12 minutes'),

    -- Period 2 Chronology (Baseline clean play, no stoppages, K = 1.0)
    ('44444444-4444-0000-0001-000000000005', v_match_id, 2, 0, v_base_time + interval '17 minutes'),
    ('44444444-4444-0000-0001-000000000006', v_match_id, 2, 1, v_base_time + interval '25 minutes')
    ON CONFLICT (id) DO NOTHING;
END $$;


-- ==============================================================================
-- 12. SEED PLAYER PRESENCES FOR MATCH 33333333-3333-0000-0000-333333333001
-- ==============================================================================
-- Logs active in-water sessions for the starting rosters of both teams.
-- Starters play the full 12 linear minutes of Period 1 to preserve K = 0.8 test.
-- Dynamically fetches match scheduled time to preserve interval integrity.
-- Implements loops with deterministic index-based UUIDs and explicit array sorting
-- via ORDER BY to guarantee absolute idempotency across execution environments.
-- ==============================================================================
DO $$ 
DECLARE 
    v_match_id uuid := '33333333-3333-0000-0000-333333333001';
    v_home_team uuid := '22222222-2222-2222-2222-222222222215';
    v_guest_team uuid := '22222222-2222-2222-2222-222222222216';
    v_home_lineups uuid[];
    v_guest_lineups uuid[];
    v_base_time timestamptz;
    v_idx int;
BEGIN
    -- Extract the relative match scheduled time to align timeline anchors
    SELECT scheduledat INTO v_base_time FROM public.matches WHERE id = v_match_id;

    -- Fetch active lineup IDs mapped during Step 10 with strict deterministic ordering
    SELECT array_agg(ml.id ORDER BY ml.id) INTO v_home_lineups FROM public.matchlineups ml 
    JOIN public.playerrosters pr ON ml.playerrosterid = pr.id WHERE ml.matchid = v_match_id AND pr.teamid = v_home_team;

    SELECT array_agg(ml.id ORDER BY ml.id) INTO v_guest_lineups FROM public.matchlineups ml 
    JOIN public.playerrosters pr ON ml.playerrosterid = pr.id WHERE ml.matchid = v_match_id AND pr.teamid = v_guest_team;

    -- Seed Period 1 Starters (First 7 players stay on field for 12 linear minutes)
    FOR v_idx IN 1..7 LOOP
        IF v_home_lineups[v_idx] IS NOT NULL THEN
            INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein, timeout) 
            VALUES (cast('55555555-5555-0000-0001-' || lpad(v_idx::text, 12, '0') as uuid), v_home_lineups[v_idx], 1, v_base_time, v_base_time + interval '12 minutes')
            ON CONFLICT (id) DO NOTHING;
        END IF;
        
        IF v_guest_lineups[v_idx] IS NOT NULL THEN
            INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein, timeout) 
            VALUES (cast('55555555-5555-0000-0002-' || lpad(v_idx::text, 12, '0') as uuid), v_guest_lineups[v_idx], 1, v_base_time, v_base_time + interval '12 minutes')
            ON CONFLICT (id) DO NOTHING;
        END IF;
    END LOOP;

    -- Seed Tactical Substitution for Period 2 (Home Player 8 replaces Home Player 1)
    IF v_home_lineups[1] IS NOT NULL AND v_home_lineups[8] IS NOT NULL THEN
        -- Player 1 plays from 0 to 3 minutes of Period 2
        INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein, timeout) 
        VALUES ('55555555-5555-0000-0003-000000000001', v_home_lineups[1], 2, v_base_time + interval '17 minutes', v_base_time + interval '20 minutes')
        ON CONFLICT (id) DO NOTHING;
        
        -- Player 8 plays remaining 5 minutes of Period 2
        INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein, timeout) 
        VALUES ('55555555-5555-0000-0003-000000000002', v_home_lineups[8], 2, v_base_time + interval '20 minutes', v_base_time + interval '25 minutes')
        ON CONFLICT (id) DO NOTHING;
    END IF;
END $$;


-- ==============================================================================
-- 13. SEED GAME EVENTS FOR MATCH 33333333-3333-0000-0000-333333333001
-- ==============================================================================
-- Populates raw technical action rows inside active game segments.
-- Normalizedmatchtime is omitted intentionally (remains NULL).
-- It will be calculated dynamically via the MatchesController normalization endpoint.
-- Strictly utilizes pre-seeded system event definitions from the database.
-- Employs static deterministic primary keys to ensure repeatable and safe deployments.
-- ==============================================================================
DO $$ 
DECLARE 
    v_match_id uuid := '33333333-3333-0000-0000-333333333001';
    v_home_team uuid := '22222222-2222-2222-2222-222222222215';
    v_guest_team uuid := '22222222-2222-2222-2222-222222222216';
    
    v_home_lineups uuid[];
    v_guest_lineups uuid[];
    
    v_goal_def_id uuid;
    v_assist_def_id uuid;
    v_excl_def_id uuid;
    v_base_time timestamptz;
    v_sport_id uuid;
BEGIN
    -- Extract the relative match scheduled time to align match timeline events
    SELECT scheduledat INTO v_base_time FROM public.matches WHERE id = v_match_id;

    -- Extract the parent sport identifier from the tournament
    SELECT t.sportid INTO v_sport_id 
    FROM public.matches m
    JOIN public.tournaments t ON m.tournamentid = t.id
    WHERE m.id = v_match_id;

    -- Fetch existing definitions from public.eventdefinitions (Resilient lookup by Name or Shortname)
    SELECT id INTO v_goal_def_id FROM public.eventdefinitions 
    WHERE (name = 'Goal' OR shortname = 'G') AND sportid = v_sport_id LIMIT 1;
    
    SELECT id INTO v_assist_def_id FROM public.eventdefinitions 
    WHERE (name = 'Assist' OR shortname = 'A') AND sportid = v_sport_id LIMIT 1;
    
    SELECT id INTO v_excl_def_id FROM public.eventdefinitions 
    WHERE (name LIKE '%Exclusion%' OR shortname = 'EX' OR shortname = 'E') AND sportid = v_sport_id LIMIT 1;

    -- Grab match lineups arrays with strict deterministic ordering to preserve index references
    SELECT array_agg(ml.id ORDER BY ml.id) INTO v_home_lineups FROM public.matchlineups ml 
    JOIN public.playerrosters pr ON ml.playerrosterid = pr.id WHERE ml.matchid = v_match_id AND pr.teamid = v_home_team;

    SELECT array_agg(ml.id ORDER BY ml.id) INTO v_guest_lineups FROM public.matchlineups ml 
    JOIN public.playerrosters pr ON ml.playerrosterid = pr.id WHERE ml.matchid = v_match_id AND pr.teamid = v_guest_team;

    -- Event 1: Guest Player 1 commits a severe foul at 2 minutes from start.
    IF v_guest_lineups[1] IS NOT NULL AND v_excl_def_id IS NOT NULL THEN
        INSERT INTO public.gameevents (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, isleadtogoal, createdat) 
        VALUES ('66666666-6666-0000-0001-000000000001', v_guest_lineups[1], v_excl_def_id, 1, v_base_time + interval '2 minutes', false, v_base_time + interval '2 minutes')
        ON CONFLICT (id) DO NOTHING;
    END IF;

    -- Event 2: Home Team Power Play! Home Player 2 makes an Assist at 3 minutes from start.
    IF v_home_lineups[2] IS NOT NULL AND v_assist_def_id IS NOT NULL THEN
        INSERT INTO public.gameevents (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, isleadtogoal, createdat) 
        VALUES ('66666666-6666-0000-0001-000000000002', v_home_lineups[2], v_assist_def_id, 1, v_base_time + interval '3 minutes', true, v_base_time + interval '3 minutes')
        ON CONFLICT (id) DO NOTHING;
    END IF;

    -- Event 3: Home Player 3 scores a Goal from that assist at 3 minutes 2 seconds from start.
    IF v_home_lineups[3] IS NOT NULL AND v_goal_def_id IS NOT NULL THEN
        INSERT INTO public.gameevents (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, isleadtogoal, createdat) 
        VALUES ('66666666-6666-0000-0001-000000000003', v_home_lineups[3], v_goal_def_id, 1, v_base_time + interval '3 minutes 2 seconds', false, v_base_time + interval '3 minutes 2 seconds')
        ON CONFLICT (id) DO NOTHING;
    END IF;

    -- Event 4: Period 2 check. Substituted Home Player 8 scores a Goal at 5 minutes into Period 2 (Base + 22 minutes).
    IF v_home_lineups[8] IS NOT NULL AND v_goal_def_id IS NOT NULL THEN
        INSERT INTO public.gameevents (id, matchlineupid, eventdefinitionid, periodnumber, eventtimestamp, isleadtogoal, createdat) 
        VALUES ('66666666-6666-0000-0001-000000000004', v_home_lineups[8], v_goal_def_id, 2, v_base_time + interval '22 minutes', false, v_base_time + interval '22 minutes')
        ON CONFLICT (id) DO NOTHING;
    END IF;
END $$;