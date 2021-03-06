using System;
using System.Collections.Generic;
using System.Text;

namespace CSLibrary.Constants
{
    /// <summary>
    /// The 16-bit packet types that will be found in the common packet header     
    /// pkt_type field.                                                            
    ///                                                                           
    /// When adding a new packet type to a class, simply append it to end of the   
    /// appropriate type's enumeration list.                                       
    ///                                                                           
    /// NOTE: These packet type constants are in the endian format for the system  
    /// upon which the compile is being performed.  Before comparing them against  
    /// the packet type field from the packet, ensure that, if necessary, the      
    /// packet type field is converted from little endian (i.e., MAC format) to    
    /// the endian format for the system running the application.                  
    /// </summary>
    enum RFID_PACKET_TYPE : ushort
    {
        /* The packet types for the common packets.                               */
        /// <summary>
        /// The command-begin packet indicates the start of a sequence of packets for an ISO 
        /// 18000-6C tag-protocol operation (i.e. inventory, read, etc.). The type of command 
        /// executed by the RFID radio module determines which data packets appear, and in 
        /// what order they appear, between the command-begin/end packet pair. 
        /// </summary>
        COMMAND_BEGIN,
        /// <summary>
        /// The command-end packet indicates the end of sequence of packets for an ISO 18000-
        /// 6C tag-protocol operation. A command-end packet is always used to terminate a 
        /// packet sequence regardless of the fact that a tag-access operation is completed 
        /// successfully or not. 
        /// </summary>
        COMMAND_END,
        /// <summary>
        /// The antenna-cycle-begin packet indicates the start of one iteration through all 
        /// enabled, functional antennas. 
        /// </summary>
        ANTENNA_CYCLE_BEGIN,
        /// <summary>
        /// The antenna-begin packet indicates the radio has begun using a particular antenna for 
        /// the current cycle. 
        /// </summary>
        ANTENNA_BEGIN,
        /// <summary>
        /// The ISO 18000-6C inventory-round-begin packet indicates that an ISO 18000-6C 
        /// inventory round has begun on an antenna. 
        /// </summary>
        ISO18K6C_INVENTORY_ROUND_BEGIN,
        /// <summary>
        /// The ISO 18000-6C inventory-response packet contains the data a tag backscatters 
        /// during the tag-singulation phase. This data is generated for tag inventories as well as 
        /// ISO 18000-6C tag-access operations (i.e. read, write, etc.). Assuming a valid CRC, 
        /// the data contains the PC+EPC+CRC16 received during the singulation of a tag. 
        /// </summary>
        ISO18K6C_INVENTORY,
        /// <summary>
        /// The ISO 18000-6C tag-access packet indicates the result of the tag-access command 
        /// upon the ISO 18000-6C tag. Valid tag access commands are as follows: 
        /// Read, Write, Kill, Lock, Erase
        /// </summary>
        ISO18K6C_TAG_ACCESS,
        /// <summary>
        /// The antenna-cycle-end packet indicates the end of one iteration through all enabled, 
        /// functional antennas. 
        /// </summary> 
        ANTENNA_CYCLE_END,
        /// <summary>
        /// The antenna-end packet indicates that the radio has stopped using a particular 
        /// antenna for the current cycle.  
        /// </summary>
        ANTENNA_END,
        /// <summary>
        /// The ISO 18000-6C inventory-round-end packet indicates that an ISO 18000-6C 
        /// inventory round has ended on an antenna. 
        /// </summary>
        ISO18K6C_INVENTORY_ROUND_END,
        /// <summary>
        /// The inventory-cycle-begin packet indicates that an inventory round has begun on an 
        /// antenna. 
        /// </summary>
        INVENTORY_CYCLE_BEGIN,
        /// <summary>
        /// The inventory-cycle-end packet indicates that an inventory round has ended on an 
        /// antenna. 
        /// </summary>
        INVENTORY_CYCLE_END,
        /// <summary>
        /// The Carrier info packet is sent by the Intel® R1000 Firmware whenever Transmit CW 
        /// is turned on and whenever it is turned off. The purpose is to provide timing 
        /// information frequency channel use information to the host application. 
        /// </summary>
        CARRIER_INFO,
        /// <summary>
        /// The Cycle configuration event packet is sent when the Intel® R1000 Firmware 
        /// performs a cycle granular configuration adjustment. 
        /// </summary>
        CYCCFG_EVT,
        /* The packet types for the diagnostics packets.                          */
        /// <summary>
        /// Reserved
        /// </summary>
        RES0 = 4096,//PktConstants.RFID_PACKET_CLASS_BASE(RFID_PACKET_CLASS.DIAGNOSTICS),
        /// <summary>
        /// Reserved
        /// </summary>
        RES1,
        /// <summary>
        /// Reserved
        /// </summary>
        RES2,
        /// <summary>
        /// Reserved
        /// </summary>
        RES3,
        /// <summary>
        /// The ISO 18000-6C inventory-round-begin-diagnostics packet appears at the beginning 
        /// of an ISO 18000-6C inventory round and contains diagnostics information related to 
        /// the inventory round that is about to commence. 
        /// </summary>
        ISO18K6C_INVENTORY_ROUND_BEGIN_DIAGS,
        /// <summary>
        /// The ISO 18000-6C inventory-round-end-diagnostics packet appears at the end of an 
        /// ISO 18000-6C inventory round and contains diagnostics information related to the 
        /// inventory round that has just completed. 
        /// </summary>
        ISO18K6C_INVENTORY_ROUND_END_DIAGS,
        /// <summary>
        /// The ISO 18000-6C inventory-response diagnostics packet is used to convey 
        /// diagnostics information for the tag during the ISO 18000-6C inventory. 
        /// </summary>
        ISO18K6C_INVENTORY_DIAGS,
        /// <summary>
        /// Reserved
        /// </summary>
        RES4,
        /// <summary>
        /// The inventory-cycle-end-diagnostics packet appears at the end of an inventory round 
        /// and contains diagnostics information related to the inventory round that has just 
        /// completed. 
        /// </summary>
        INVENTORY_CYCLE_END_DIAGS,
        /* Custom API for Level Low*/
        LOW_LEVEL_COMMAND_BEGIN = 0x8000,
        LOW_LEVEL_COMMAND_END = 0x8001,
        LOW_LEVEL_ISO18K6C_INVENTORY = 0x8005,
        /// <summary>
        /// The packet types for the status packets. 
        /// </summary>
        NONCRITICAL_FAULT = 8192,//PktConstants.RFID_PACKET_CLASS_BASE(RFID_PACKET_CLASS.STATUS),
        ENGTSTPAT_ZZS = 3 << 12,
        ENGTSTPAT_FFS,
        ENGTSTPAT_W1S,
        ENGTSTPAT_W0S,
        ENGTSTPAT_BND,
        MBP_READ_REG,
        GPIO_READ,
        OEMCFG_READ,
        ENG_RSSI,
        ENG_INVSTATS,
        ENG_BERTSTRESULT,
        NVMEMUPDCFG,
        LPROF_READ_REG,

        UNKNOWN = 0xf000,
    }
}
